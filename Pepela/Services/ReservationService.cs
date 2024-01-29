// ReservationService.cs
// Author: Ondřej Ondryáš

using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NodaTime;
using Pepela.Configuration;
using Pepela.Data;
using Pepela.Jobs;
using Pepela.Models;
using Quartz;

namespace Pepela.Services;

public class ReservationService
{
    private const string SeatsLeftCacheKey = "SeatsLeft";

    private readonly AppDbContext _dbContext;
    private readonly EmailService _emailService;
    private readonly LinkService _linkService;
    private readonly ISchedulerFactory _schedulerFactory;
    private readonly IMemoryCache _cache;
    private readonly IOptions<SeatsOptions> _seatsOptions;
    private readonly ILogger<ReservationService> _logger;

    public ReservationService(AppDbContext dbContext, EmailService emailService, LinkService linkService,
        ISchedulerFactory schedulerFactory, IMemoryCache cache, IOptions<SeatsOptions> seatsOptions,
        ILogger<ReservationService> logger)
    {
        _dbContext = dbContext;
        _emailService = emailService;
        _linkService = linkService;
        _schedulerFactory = schedulerFactory;
        _cache = cache;
        _seatsOptions = seatsOptions;
        _logger = logger;
    }

    public async Task<ReservationAttemptResult> MakeReservation(ReservationModel model, bool force = false,
        bool mustConfirm = true)
    {
        var originalMail = model.Email;
        model.Email = model.Email.ToUpperInvariant();

        var existing = await _dbContext.Reservations.Where(r => r.Email == model.Email)
                                       .FirstOrDefaultAsync();

        if (existing is { Confirmed: true, Cancelled: false })
            return ReservationAttemptResult.EmailTaken;

        if (!force)
        {
            var left = await this.GetSeatsLeft() + this.GetSeatsCountedInReservation(existing);

            if ((left - model.Seats) < 0)
                return ReservationAttemptResult.NoSeatsLeft;
        }

        using var rng = RandomNumberGenerator.Create();
        var array = new byte[32];
        rng.GetNonZeroBytes(array);
        var token = Convert.ToHexString(array);

        if (existing != null)
        {
            _dbContext.Remove(existing);
        }

        var entity = new ReservationEntity()
        {
            ManagementToken = token,
            Email = model.Email,
            MadeOn = SystemClock.Instance.GetCurrentInstant(),
            Seats = model.Seats,
            ConfirmedOn = mustConfirm ? null : SystemClock.Instance.GetCurrentInstant()
        };

        _dbContext.Add(entity);
        try
        {
            await _dbContext.SaveChangesAsync();
            _cache.Remove(SeatsLeftCacheKey);
        }
        catch (DbUpdateException e)
        {
            _logger.LogError(e, "Error saving reservation.");

            return ReservationAttemptResult.Error;
        }

        if (existing != null)
        {
            await _emailService.SendCancelledEmail(existing.Email, existing.Seats, existing.MadeOn);
        }

        if (mustConfirm)
        {
            await _emailService.SendConfirmationMail(originalMail,
                _linkService.MakeConfirmLink(originalMail, entity.ManagementToken),
                _linkService.MakeCancelLink(originalMail, entity.ManagementToken));
        }

        return ReservationAttemptResult.MustConfirm;
    }

    public async Task<ReservationCompletionResult> ConfirmReservation(string email, string? token, bool force = false)
    {
        var originalMail = email;
        email = email.ToUpperInvariant();

        var reservation = await _dbContext.Reservations.Where(r => r.Email == email)
                                          .FirstOrDefaultAsync();

        if (reservation == null || reservation.Cancelled)
            return ReservationCompletionResult.NotFound;

        if (token != null && reservation.ManagementToken != token)
            return ReservationCompletionResult.InvalidToken;

        if (reservation.Confirmed)
            return ReservationCompletionResult.AlreadyConfirmed;

        if (!force)
        {
            var left = await this.GetSeatsLeft() + this.GetSeatsCountedInReservation(reservation);

            if ((left - reservation.Seats) < 0)
                return ReservationCompletionResult.NoSeatsLeft;
        }

        reservation.ConfirmedOn = SystemClock.Instance.GetCurrentInstant();
        try
        {
            await _dbContext.SaveChangesAsync();
            _cache.Remove(SeatsLeftCacheKey);
            await _emailService.SendDoneMail(originalMail, reservation.Seats,
                _linkService.MakeCancelLink(originalMail, reservation.ManagementToken));
        }
        catch (DbUpdateException e)
        {
            _logger.LogError(e, "Error completing reservation.");

            return ReservationCompletionResult.Error;
        }

        return ReservationCompletionResult.Confirmed;
    }

    public async Task<ReservationCompletionResult> CancelReservation(string email, string? token)
    {
        var originalMail = email;
        email = email.ToUpperInvariant();

        var reservation = await _dbContext.Reservations.Where(r => r.Email == email)
                                          .FirstOrDefaultAsync();

        if (reservation == null)
            return ReservationCompletionResult.NotFound;

        if (token != null && reservation.ManagementToken != token)
            return ReservationCompletionResult.InvalidToken;

        if (reservation.Cancelled)
            return ReservationCompletionResult.AlreadyConfirmed;

        reservation.CancelledOn = SystemClock.Instance.GetCurrentInstant();
        try
        {
            await _dbContext.SaveChangesAsync();
            _cache.Remove(SeatsLeftCacheKey);
            await _emailService.SendCancelledEmail(originalMail, reservation.Seats, reservation.MadeOn);
        }
        catch (DbUpdateException e)
        {
            _logger.LogError(e, "Error cancelling reservation.");

            if (!reservation.Confirmed)
                return ReservationCompletionResult.AlreadyConfirmed;

            return ReservationCompletionResult.Error;
        }

        return ReservationCompletionResult.Confirmed;
    }

    public async Task<ReservationEntity?> GetReservationDetails(string email)
    {
        email = email.ToUpperInvariant();

        return await _dbContext.Reservations.FirstOrDefaultAsync(r => r.Email == email);
    }

    public async Task SendReminderEmailToAll(CancellationToken cancellationToken = default)
    {
        var reservations = _dbContext.Reservations
                                     .Where(r => r.ConfirmedOn != null && r.CancelledOn == null)
                                     .AsAsyncEnumerable();

        var scheduler = await _schedulerFactory.GetScheduler(cancellationToken);

        await foreach (var reservation in reservations.WithCancellation(cancellationToken))
        {
            if (reservation is null or {Email: null} or {ManagementToken: null})
                continue;
            
            try
            {
                var link = _linkService.MakeCancelLink(reservation.Email, reservation.ManagementToken);
                var job = JobBuilder.Create<SendReminderEmailJob>()
                                    .UsingJobData(new JobDataMap()
                                    {
                                        { "email", reservation.Email },
                                        { "seats", reservation.Seats },
                                        { "link", link }
                                    })
                                    .WithIdentity(reservation.Id.ToString(), "reminder-email")
                                    .Build();

                var trigger = TriggerBuilder.Create()
                                            .WithIdentity(reservation.Id.ToString(), "reminder-email")
                                            .ForJob(job)
                                            .StartNow()
                                            .Build();

                await scheduler.ScheduleJob(job, trigger, cancellationToken);
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Error scheduling reminder mail to {Email}.", reservation.Email);
            }
        }
    }

    public IAsyncEnumerable<ReservationEntity> GetConfirmedReservations()
    {
        return _dbContext.Reservations
                         .Where(r => r.ConfirmedOn != null && r.CancelledOn == null)
                         .AsAsyncEnumerable();
    }

    public async Task<string> MakeConfirmedReservationsCsv()
    {
        var sb = new StringBuilder();
        sb.AppendLine("mail;seats");

        await foreach (var reservation in this.GetConfirmedReservations())
        {
            sb.AppendLine($"{reservation.Email};{reservation.Seats}");
        }

        return sb.ToString();
    }

    public async Task<int> GetSeatsLeft(bool cached = false)
    {
        if (cached && _cache.TryGetValue(SeatsLeftCacheKey, out int seatsLeft))
            return seatsLeft;

        var total = await this.GetTotalSeats();

        var now = SystemClock.Instance.GetCurrentInstant();
        var unconfirmedValidMinutes = Duration.FromMinutes(_seatsOptions.Value.UnconfirmedValidMinutes);

        var taken = await _dbContext.Reservations.Where(r => r.CancelledOn == null &&
                                        (r.ConfirmedOn != null || (now - r.MadeOn) < unconfirmedValidMinutes))
                                    .SumAsync(r => r.Seats);

        seatsLeft = int.Max(0, total - taken);
        _cache.Set(SeatsLeftCacheKey, seatsLeft, seatsLeft < 2
            ? TimeSpan.FromMinutes(_seatsOptions.Value.UnconfirmedValidMinutes)
            : TimeSpan.FromHours(1));

        return seatsLeft;
    }

    private int GetSeatsCountedInReservation(ReservationEntity? entity)
    {
        if (entity == null)
            return 0;

        return SystemClock.Instance.GetCurrentInstant() - entity.MadeOn <
            Duration.FromMinutes(_seatsOptions.Value.UnconfirmedValidMinutes)
                ? entity.Seats
                : 0;
    }

    public ValueTask<int> GetTotalSeats()
    {
        return ValueTask.FromResult(_seatsOptions.Value.TotalSeats);
    }
}
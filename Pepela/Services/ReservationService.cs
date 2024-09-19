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
    private const string PubQuizSeatsLeftCacheKey = "PubQuizSeatsLeft";
    private const string TimeSlotSeatsLeftCacheKey = "TimeSlot.{0}.SeatsLeft";

    private readonly AppDbContext _dbContext;
    private readonly EmailService _emailService;
    private readonly LinkService _linkService;
    private readonly ISchedulerFactory _schedulerFactory;
    private readonly IMemoryCache _cache;
    private readonly IOptions<SeatsOptions> _seatsOptions;
    private readonly ILogger<ReservationService> _logger;
    private readonly DateTimeZone _zone;

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
        _zone = DateTimeZoneProviders.Tzdb["Europe/Prague"];
    }

    public async Task<ReservationAttemptResult> MakeReservation(ReservationModel model, bool force = false,
        bool mustConfirm = true)
    {
        var originalMail = model.Email;
        model.Email = model.Email.ToLowerInvariant();

        var existing = await _dbContext.Reservations.Where(r => r.Email == model.Email)
                                       .Include(r => r.AssociatedTimeSlots)
                                       .FirstOrDefaultAsync();

        if (existing is { Confirmed: true, Cancelled: false })
            return ReservationAttemptResult.EmailTaken;

        if (!force)
        {
            if (!await this.CheckSeatsLeftForReservation(existing, model.Seats))
                return ReservationAttemptResult.NoSeatsLeft;

            if (!await this.CheckPubQuizTeamsLeftForReservation(existing))
                return ReservationAttemptResult.NoPubQuizTeamsLeft;

            if (model.EscapeASelectedId != null &&
                !await this.CheckSlotSeatsLeftForReservation(model.EscapeASelectedId.Value, existing))
                return ReservationAttemptResult.TimeslotError;

            if (model.EscapeBSelectedId != null &&
                !await this.CheckSlotSeatsLeftForReservation(model.EscapeBSelectedId.Value, existing))
                return ReservationAttemptResult.TimeslotError;
        }

        using var rng = RandomNumberGenerator.Create();
        var array = new byte[32];
        rng.GetNonZeroBytes(array);
        var token = Convert.ToHexString(array);

        if (existing != null)
            _dbContext.Remove(existing);

        var associatedTimeSlots = new List<TimeSlotEntity>();
        if (model.EscapeASelectedId != null)
        {
            var slot = await _dbContext.TimeSlots
                                       .FirstOrDefaultAsync(x => x.Id == model.EscapeASelectedId);
            if (slot != null)
                associatedTimeSlots.Add(slot);
        }

        if (model.EscapeBSelectedId != null)
        {
            var slot = await _dbContext.TimeSlots
                                       .FirstOrDefaultAsync(x => x.Id == model.EscapeBSelectedId);
            if (slot != null)
                associatedTimeSlots.Add(slot);
        }

        var entity = new ReservationEntity()
        {
            ManagementToken = token,
            Email = model.Email,
            MadeOn = SystemClock.Instance.GetCurrentInstant(),
            Seats = model.Seats,
            ConfirmedOn = mustConfirm ? null : SystemClock.Instance.GetCurrentInstant(),

            SleepOver = model.SleepOver,
            PubQuizTeamName = model.PubQuizTeamName,
            PubQuizSeats = model.PubQuizSeats ?? _seatsOptions.Value.MinPubQuizTeamSize,

            AssociatedTimeSlots = associatedTimeSlots
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
                _linkService.MakeCancelLink(originalMail, entity.ManagementToken),
                this.GetEmailExtras(entity));
        }

        return ReservationAttemptResult.MustConfirm;
    }

    public async Task<ReservationCompletionResult> ConfirmReservation(string email, string? token, bool force = false)
    {
        var originalMail = email;
        email = email.ToLowerInvariant();

        var reservation = await _dbContext.Reservations.Where(r => r.Email == email)
                                          .Include(r => r.AssociatedTimeSlots)
                                          .FirstOrDefaultAsync();

        if (reservation == null || reservation.Cancelled)
            return ReservationCompletionResult.NotFound;

        if (token != null && reservation.ManagementToken != token)
            return ReservationCompletionResult.InvalidToken;

        if (reservation.Confirmed)
            return ReservationCompletionResult.AlreadyConfirmed;

        if (!force)
        {
            if (!await this.CheckSeatsLeftForReservation(reservation, reservation.Seats))
                return ReservationCompletionResult.NoSeatsLeft;

            if (!await this.CheckPubQuizTeamsLeftForReservation(reservation))
                return ReservationCompletionResult.NoPubQuizTeamsLeft;

            foreach (var slot in reservation.AssociatedTimeSlots)
            {
                if (!await this.CheckSlotSeatsLeftForReservation(slot, reservation))
                    return ReservationCompletionResult.TimeslotError;
            }
        }

        reservation.ConfirmedOn = SystemClock.Instance.GetCurrentInstant();
        try
        {
            await _dbContext.SaveChangesAsync();
            _cache.Remove(SeatsLeftCacheKey);
            await _emailService.SendDoneMail(originalMail, reservation.Seats,
                _linkService.MakeCancelLink(originalMail, reservation.ManagementToken),
                this.GetEmailExtras(reservation));
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
        email = email.ToLowerInvariant();

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
        email = email.ToLowerInvariant();

        return await _dbContext.Reservations
                               .Include(r => r.AssociatedTimeSlots)
                               .FirstOrDefaultAsync(r => r.Email == email);
    }

    public async Task SendReminderEmailToAll(CancellationToken cancellationToken = default)
    {
        var reservations = _dbContext.Reservations
                                     .Where(r => r.ConfirmedOn != null && r.CancelledOn == null)
                                     .AsAsyncEnumerable();

        var scheduler = await _schedulerFactory.GetScheduler(cancellationToken);

        await foreach (var reservation in reservations.WithCancellation(cancellationToken))
        {
            if (reservation is null or { Email: null } or { ManagementToken: null })
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

    private IAsyncEnumerable<ReservationEntity> GetConfirmedReservations()
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

    public async Task<int> GetPubQuizTeamsLeft(bool cached = false)
    {
        if (cached && _cache.TryGetValue(PubQuizSeatsLeftCacheKey, out int seatsLeft))
            return seatsLeft;

        var total = await this.GetTotalPubQuizTeams();

        var now = SystemClock.Instance.GetCurrentInstant();
        var unconfirmedValidMinutes = Duration.FromMinutes(_seatsOptions.Value.UnconfirmedValidMinutes);

        var taken = await _dbContext.Reservations.Where(r => r.CancelledOn == null &&
                                        (r.ConfirmedOn != null || (now - r.MadeOn) < unconfirmedValidMinutes) &&
                                        r.PubQuizTeamName != null)
                                    .CountAsync();

        seatsLeft = int.Max(0, total - taken);
        _cache.Set(PubQuizSeatsLeftCacheKey, seatsLeft, seatsLeft < 2
            ? TimeSpan.FromMinutes(_seatsOptions.Value.UnconfirmedValidMinutes)
            : TimeSpan.FromHours(1));

        return seatsLeft;
    }

    public async Task<int> GetSlotSeatsLeft(int timeSlotId, bool cached = false)
    {
        var cacheKey = string.Format(TimeSlotSeatsLeftCacheKey, timeSlotId);

        if (cached && _cache.TryGetValue(cacheKey, out int seatsLeft))
            return seatsLeft;

        var entity = await _dbContext.TimeSlots
                                     .Include(x => x.AssociatedReservations)
                                     .FirstOrDefaultAsync(x => x.Id == timeSlotId);

        if (entity == null)
            return 0;

        var total = entity.TotalSeats;
        var now = SystemClock.Instance.GetCurrentInstant();
        var unconfirmedValidMinutes = Duration.FromMinutes(_seatsOptions.Value.UnconfirmedValidMinutes);

        var taken = entity.AssociatedReservations.Where(r => r.CancelledOn == null &&
                              (r.ConfirmedOn != null || (now - r.MadeOn) < unconfirmedValidMinutes))
                          .Sum(r => entity.AlwaysConsumeOnePerReservation ? 1 : r.Seats);

        seatsLeft = int.Max(0, total - taken);
        _cache.Set(cacheKey, seatsLeft, seatsLeft < 2
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

    private int GetPubQuizTeamsCountedInReservation(ReservationEntity? entity)
    {
        if (entity is not { HasPubQuizTeam: true })
            return 0;

        return SystemClock.Instance.GetCurrentInstant() - entity.MadeOn <
            Duration.FromMinutes(_seatsOptions.Value.UnconfirmedValidMinutes)
                ? 1
                : 0;
    }

    private int GetSlotSeatsCountedInReservation(TimeSlotEntity? timeSlot, ReservationEntity? entity)
    {
        if (entity == null)
            return 0;

        if (timeSlot == null)
            return 0;

        if (SystemClock.Instance.GetCurrentInstant() - entity.MadeOn <
            Duration.FromMinutes(_seatsOptions.Value.UnconfirmedValidMinutes))
            return timeSlot.AlwaysConsumeOnePerReservation ? 1 : entity.Seats;

        return 0;
    }

    public ValueTask<int> GetTotalSeats()
    {
        return ValueTask.FromResult(_seatsOptions.Value.TotalSeats);
    }

    public ValueTask<int> GetTotalPubQuizTeams()
    {
        return ValueTask.FromResult(_seatsOptions.Value.TotalPubQuizTeams);
    }

    private async Task<bool> CheckSeatsLeftForReservation(ReservationEntity? reservation, int seats)
    {
        var left = await this.GetSeatsLeft() + this.GetSeatsCountedInReservation(reservation);

        return left - seats >= 0;
    }

    private async Task<bool> CheckPubQuizTeamsLeftForReservation(ReservationEntity? reservation)
    {
        if (reservation is not { HasPubQuizTeam: true })
            return true;

        var left = await this.GetPubQuizTeamsLeft() + this.GetPubQuizTeamsCountedInReservation(reservation);

        return left - 1 >= 0;
    }

    private async Task<bool> CheckSlotSeatsLeftForReservation(int timeSlotId, ReservationEntity? reservation)
    {
        var timeSlot = await _dbContext.TimeSlots.FirstOrDefaultAsync(x => x.Id == timeSlotId);

        return await this.CheckSlotSeatsLeftForReservation(timeSlot, reservation);
    }

    private async Task<bool> CheckSlotSeatsLeftForReservation(TimeSlotEntity? timeSlot, ReservationEntity? reservation)
    {
        if (reservation == null)
            return true;

        if (timeSlot == null)
            return false;

        var left = await this.GetSlotSeatsLeft(timeSlot.Id) +
            this.GetSlotSeatsCountedInReservation(timeSlot, reservation);

        var seatsToConsume = timeSlot.AlwaysConsumeOnePerReservation ? 1 : reservation.Seats;

        return left - seatsToConsume >= 0;
    }

    public async Task<List<SlottedActivity>> GetSlottedActivities()
    {
        return await _dbContext.Activities.Select(x => new SlottedActivity()
        {
            Id = x.Id,
            Name = x.Name
        }).ToListAsync();
    }

    private IEnumerable<TimeSlot> GetEmailExtras(ReservationEntity reservationEntity)
    {
        var extras = reservationEntity.AssociatedTimeSlots
                                      .Select(x => this.ToModel(x, 0));
        if (reservationEntity.PubQuizTeamName != null)
        {
            var pubQuizTimeSlot = new TimeSlot()
            {
                Id = -1,
                Start = Instant.FromUtc(2024, 9, 27, 17, 0).InZone(_zone),
                End = Instant.FromUtc(2024, 9, 27, 22, 0).InZone(_zone),
                ActivityName = "Pubkvíz",
                Note = $"Tým {reservationEntity.PubQuizTeamName}",
                TotalSeats = -1, AlwaysConsumeOnePerReservation = false, AvailableSeats = -1
            };
            extras = extras.Append(pubQuizTimeSlot);
        }

        return extras;
    }

    private TimeSlot ToModel(TimeSlotEntity slotEntity, int availableSeats)
    {
        return new TimeSlot()
        {
            Id = slotEntity.Id,
            Start = slotEntity.Start.InZone(_zone),
            End = slotEntity.End.InZone(_zone),
            ActivityName = slotEntity.Activity.Name,
            TotalSeats = slotEntity.TotalSeats,
            AvailableSeats = availableSeats,
            Note = slotEntity.Note,
            AlwaysConsumeOnePerReservation = slotEntity.AlwaysConsumeOnePerReservation
        };
    }

    public async Task<List<TimeSlot>> GetTimeslotsForActivity(int slottedActivityId)
    {
        var slots = await _dbContext.TimeSlots
                                    .Where(x => x.ActivityId == slottedActivityId)
                                    .Include(x => x.Activity)
                                    .Include(x => x.AssociatedReservations)
                                    .ToListAsync();

        var ret = new List<TimeSlot>();
        var now = SystemClock.Instance.GetCurrentInstant();
        var unconfirmedValidMinutes = Duration.FromMinutes(_seatsOptions.Value.UnconfirmedValidMinutes);

        foreach (var slotEntity in slots)
        {
            var availableSeats = slotEntity.TotalSeats
                - slotEntity.AssociatedReservations
                            .Where(r => r.CancelledOn == null &&
                                (r.ConfirmedOn != null || (now - r.MadeOn) < unconfirmedValidMinutes))
                            .Sum(x => slotEntity.AlwaysConsumeOnePerReservation ? 1 : x.Seats);

            var slotModel = this.ToModel(slotEntity, availableSeats);
            ret.Add(slotModel);
        }

        return ret;
    }
}
// ReservationService.cs
// Author: Ondřej Ondryáš

using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    private const string TimeSlotSeatsLeftCacheKey = "TimeSlot.{0}.SeatsLeft";

    public const int EscapeAActivityId = 1;
    public const int EscapeBActivityId = 2;
    public const int PubQuizTeamsActivityId = 3;
    public const int PubQuizSoloActivityId = 4;
    public const int PubQuizTeamsTimeSlotId = 25;
    public const int PubQuizSoloTimeSlotId = 26;

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

            async Task<ReservationAttemptResult> GetTimeslotErrorResult(int id)
            {
                var timeSlot =
                    await _dbContext.TimeSlots
                        .Include(x => x.Activity)
                        .FirstOrDefaultAsync(x => x.Id == id);
                if (timeSlot == null)
                    return new ReservationAttemptResult(ReservationAttemptResultCode.TimeslotError,
                        null, "Time slot not found.");

                return new ReservationAttemptResult(ReservationAttemptResultCode.TimeslotError,
                    this.ToModel(timeSlot, 0));
            }

            if (model.EscapeASelectedId != null &&
                !await this.CheckSlotSeatsLeftForReservation(model.EscapeASelectedId.Value, existing, model.Seats))
                return await GetTimeslotErrorResult(model.EscapeASelectedId.Value);

            if (model.EscapeBSelectedId != null &&
                !await this.CheckSlotSeatsLeftForReservation(model.EscapeBSelectedId.Value, existing, model.Seats))
                return await GetTimeslotErrorResult(model.EscapeBSelectedId.Value);

            if (model.PubQuizTeamName != null)
            {
                var quizSlotId = model.PubQuizSeats == 1 ? PubQuizSoloTimeSlotId : PubQuizTeamsTimeSlotId;
                if (!await this.CheckSlotSeatsLeftForReservation(quizSlotId, existing, model.Seats))
                    return await GetTimeslotErrorResult(quizSlotId);
            }
        }

        using var rng = RandomNumberGenerator.Create();
        var array = new byte[32];
        rng.GetNonZeroBytes(array);
        var token = Convert.ToHexString(array);

        if (existing != null)
            _dbContext.Remove(existing);

        var associatedTimeSlots = new List<ReservationTimeSlotAssociation>();

        void AddSlotEntity(int id)
        {
            associatedTimeSlots.Add(new ReservationTimeSlotAssociation()
            {
                TimeSlotId = id,
                TakenTimeSlotSeats = 1 // Not used now
            });
        }

        if (model.EscapeASelectedId != null)
            AddSlotEntity(model.EscapeASelectedId.Value);

        if (model.EscapeBSelectedId != null)
            AddSlotEntity(model.EscapeBSelectedId.Value);

        if (model.PubQuizTeamName != null)
        {
            model.PubQuizSeats ??= _seatsOptions.Value.MinPubQuizTeamSize;
            if (model.PubQuizSeats == 1)
                AddSlotEntity(PubQuizSoloTimeSlotId);
            else
                AddSlotEntity(PubQuizTeamsTimeSlotId);
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

            TimeSlotAssociations = associatedTimeSlots
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

            return ReservationAttemptResult.Error("Database update exception.");
        }

        if (existing != null)
        {
            await _emailService.SendCancelledEmail(existing.Email, existing.Seats, existing.MadeOn);
        }

        entity = await _dbContext.Reservations.Include(x => x.AssociatedTimeSlots)
            .ThenInclude(x => x.Activity)
            .FirstOrDefaultAsync(x => x.Id == entity.Id);

        if (entity == null)
        {
            _logger.LogError("Reservation saving inconsistency: entity not found after save.");
            return ReservationAttemptResult.Error("Reservation saving inconsistency.");
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
            .ThenInclude(x => x.Activity)
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

            foreach (var slot in reservation.AssociatedTimeSlots)
            {
                if (!await this.CheckSlotSeatsLeftForReservation(slot, reservation, reservation.Seats))
                    return new ReservationCompletionResult(ReservationCompletionResultCode.TimeslotError,
                        this.ToModel(slot, 0));
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

            return ReservationCompletionResult.Error("Database update exception");
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

            return ReservationCompletionResult.Error("Database update exception.");
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
            .Include(r => r.AssociatedTimeSlots)
            .ThenInclude(ts => ts.Activity)
            .AsAsyncEnumerable();

        var scheduler = await _schedulerFactory.GetScheduler(cancellationToken);

        await foreach (var reservation in reservations.WithCancellation(cancellationToken))
        {
            if (reservation is null or { Email: null } or { ManagementToken: null })
                continue;

            try
            {
                var link = _linkService.MakeCancelLink(reservation.Email, reservation.ManagementToken);
                var slots = JsonSerializer.Serialize(this.GetEmailExtras(reservation)
                    .Select(x => new TimeSlotForJob(x)).ToImmutableArray());

                var job = JobBuilder.Create<SendReminderEmailJob>()
                    .UsingJobData(new JobDataMap()
                    {
                        { "email", reservation.Email },
                        { "seats", reservation.Seats },
                        { "link", link },
                        { "timeSlots", slots }
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
            .Include(r => r.AssociatedTimeSlots)
            .AsAsyncEnumerable();
    }

    public async Task<string> MakeConfirmedReservationsCsv()
    {
        var sb = new StringBuilder();
        sb.AppendLine("mail;seats;sleep;pubquiz_team_name;pubquiz_team_size;sklep;kafka");

        await foreach (var reservation in this.GetConfirmedReservations())
        {
            var escapeA = reservation.AssociatedTimeSlots.FirstOrDefault(x => x.ActivityId == EscapeAActivityId);
            var escapeB = reservation.AssociatedTimeSlots.FirstOrDefault(x => x.ActivityId == EscapeBActivityId);
            var escapeATime = escapeA == null
                ? "-"
                : $"{escapeA.Start.InZone(_zone):HH:mm}–{escapeA.End.InZone(_zone):HH:mm}";
            var escapeBTime = escapeB == null
                ? "-"
                : $"{escapeB.Start.InZone(_zone):HH:mm}–{escapeB.End.InZone(_zone):HH:mm}";

            sb.AppendLine(
                $"{reservation.Email};{reservation.Seats};{reservation.SleepOver};{reservation.PubQuizTeamName?.Replace(';', '-') ?? "-"};{reservation.PubQuizSeats};{escapeATime};{escapeBTime}");
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
                                                             (r.ConfirmedOn != null ||
                                                              (now - r.MadeOn) < unconfirmedValidMinutes))
            .SumAsync(r => r.Seats);

        seatsLeft = int.Max(0, total - taken);
        _cache.Set(SeatsLeftCacheKey, seatsLeft, seatsLeft < 2
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
            .Include(x => x.ReservationAssociations)
            .ThenInclude(x => x.Reservation)
            .FirstOrDefaultAsync(x => x.Id == timeSlotId);

        if (entity == null)
            return -1;

        var total = entity.TotalSeats;
        var now = SystemClock.Instance.GetCurrentInstant();
        var unconfirmedValidMinutes = Duration.FromMinutes(_seatsOptions.Value.UnconfirmedValidMinutes);

        var taken = entity.ReservationAssociations.Where(r => r.Reservation.CancelledOn == null &&
                                                              (r.Reservation.ConfirmedOn != null ||
                                                               (now - r.Reservation.MadeOn) < unconfirmedValidMinutes))
            .Sum(r => r.TakenTimeSlotSeats);

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

    private async Task<bool> CheckSeatsLeftForReservation(ReservationEntity? reservation, int seats)
    {
        var left = await this.GetSeatsLeft() + this.GetSeatsCountedInReservation(reservation);

        return left - seats >= 0;
    }

    private async Task<bool> CheckSlotSeatsLeftForReservation(int timeSlotId, ReservationEntity? reservation, int seats)
    {
        var timeSlot = await _dbContext.TimeSlots.FirstOrDefaultAsync(x => x.Id == timeSlotId);

        return await this.CheckSlotSeatsLeftForReservation(timeSlot, reservation, seats);
    }

    private async Task<bool> CheckSlotSeatsLeftForReservation(TimeSlotEntity? timeSlot, ReservationEntity? reservation,
        int seats)
    {
        if (timeSlot == null)
            return false;

        var left = await this.GetSlotSeatsLeft(timeSlot.Id) +
                   this.GetSlotSeatsCountedInReservation(timeSlot, reservation);

        var seatsToConsume = timeSlot.AlwaysConsumeOnePerReservation ? 1 : seats;

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
        return extras;
    }

    private TimeSlot ToModel(TimeSlotEntity slotEntity, int availableSeats)
    {
        return new TimeSlot()
        {
            Id = slotEntity.Id,
            ActivityId = slotEntity.ActivityId,
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

    public async Task<(bool Teams, bool Solo)> GetPubQuizAvailability(bool cached)
    {
        var teamsSlots = await this.GetTimeslotsForActivity(PubQuizTeamsActivityId);
        var soloSlots = await this.GetTimeslotsForActivity(PubQuizSoloActivityId);

        return (teamsSlots[0].AvailableSeats > 0, soloSlots[0].AvailableSeats > 0);
    }
}
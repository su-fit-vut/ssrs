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

    public const int PubQuizTeamsActivityId = 1;
    public const int PubQuizSoloActivityId = 2;
    public const int PubQuizTeamsTimeSlotId = 1;
    public const int PubQuizSoloTimeSlotId = 2;

    private readonly AppDbContext _dbContext;
    private readonly EmailService _emailService;
    private readonly LinkService _linkService;
    private readonly ISchedulerFactory _schedulerFactory;
    private readonly IMemoryCache _cache;
    private readonly IOptions<SeatsOptions> _seatsOptions;
    private readonly SemaphoreSlim _slotCountLock;
    private readonly ILogger<ReservationService> _logger;
    private readonly DateTimeZone _zone;

    public ReservationService(AppDbContext dbContext, EmailService emailService, LinkService linkService,
        ISchedulerFactory schedulerFactory, IMemoryCache cache, IOptions<SeatsOptions> seatsOptions,
        [FromKeyedServices("SlotCountLock")] SemaphoreSlim slotCountLock,
        ILogger<ReservationService> logger)
    {
        _dbContext = dbContext;
        _emailService = emailService;
        _linkService = linkService;
        _schedulerFactory = schedulerFactory;
        _cache = cache;
        _seatsOptions = seatsOptions;
        _slotCountLock = slotCountLock;
        _logger = logger;
        _zone = DateTimeZoneProviders.Tzdb["Europe/Prague"];
    }

    private async Task<ReservationAttemptResult> GetTimeslotErrorResult(int id)
    {
        var timeSlot =
            await _dbContext.TimeSlots
                .Include(x => x.Activity)
                .FirstOrDefaultAsync(x => x.Id == id);

        if (timeSlot == null)
            return new ReservationAttemptResult(ReservationAttemptResultCode.TimeslotError,
                null, null, "Time slot not found.");

        return new ReservationAttemptResult(ReservationAttemptResultCode.TimeslotError,
            this.ToModel(timeSlot, 0));
    }

    private async Task<TimeSlotEntity?> GetTimeSlotIfUnreservableNow(List<int> ids)
    {
        var now = SystemClock.Instance.GetCurrentInstant();

        var slot = await _dbContext.TimeSlots.FirstOrDefaultAsync(x =>
            ids.Contains(x.Id) && ((x.ReserveBefore != null && now > x.ReserveBefore) ||
                                   (x.ReserveAfter != null && now < x.ReserveAfter)));

        return slot;
    }

    public async Task RemoveTimeSlotFromReservation(string email, int timeslotId)
    {
        var reservation = await _dbContext.Reservations
            .Where(x => x.Email == email)
            .Include(x => x.AssociatedTimeSlots)
            .FirstOrDefaultAsync();

        var removed = reservation?.AssociatedTimeSlots.RemoveAll(x => x.Id == timeslotId);

        if (removed is not null and not 0)
        {
            try
            {
                await _dbContext.SaveChangesAsync();
                this.ClearTimeSlotCache(timeslotId);
            }
            catch (DbUpdateException e)
            {
                _logger.LogError(e, "Error saving reservation.");
            }

            await this.CancelReservationIfEmpty(email);
        }
    }

    private async Task<ReservationAttemptResult> UpdateReservation(ReservationModel model, ReservationEntity existing,
        bool force = false)
    {
        model.Seats = existing.Seats;

        // IDs of activities to remove all the currently registered slots for
        var activityIdsToRemoveSlotsFor = new List<int>();
        // IDs of time slots to newly associate with the existing reservation
        var slotIdsToAdd = new List<int>();

        if (!force && !await _slotCountLock.WaitAsync(_seatsOptions.Value.LockTimeoutMs))
            return ReservationAttemptResult.Timeout;

        // PubQuiz: Cannot modify quiz associations this way
        model.SelectedTimeSlotIds.Remove(PubQuizTeamsActivityId);
        model.SelectedTimeSlotIds.Remove(PubQuizSoloActivityId);

        try
        {
            // Check if there is space in the newly selected slots
            // and populate the lists
            foreach (var selection in model.SelectedTimeSlotIds)
            {
                if (selection.Value == null)
                {
                    activityIdsToRemoveSlotsFor.Add(selection.Key);
                }
                // Don't check for collisions with the time slot reservations that have not changed! 
                else if (existing.AssociatedTimeSlots.All(x => x.Id != selection.Value))
                {
                    if (!force && !await this.CheckSlotSeatsLeftForReservation(selection.Value.Value, existing,
                            model.Seats))
                        return await this.GetTimeslotErrorResult(selection.Value.Value);

                    slotIdsToAdd.Add(selection.Value.Value);
                    activityIdsToRemoveSlotsFor.Add(selection.Key);
                }
            }

            // Check if any of the new slots is unreservable at this time
            if (!force && await this.GetTimeSlotIfUnreservableNow(slotIdsToAdd) is { } unreservable)
                return new ReservationAttemptResult(ReservationAttemptResultCode.TimeslotNotReservable,
                    this.ToModel(unreservable, 0));

            var timeSlotsTakeOneSeat = await _dbContext.TimeSlots
                .Where(x => slotIdsToAdd.Contains(x.Id))
                .Select(x => new { x.Id, x.AlwaysConsumeOnePerReservation })
                .ToDictionaryAsync(x => x.Id, x => x.AlwaysConsumeOnePerReservation);

            // Create associations for the newly added slots
            foreach (var newSlotId in slotIdsToAdd)
            {
                existing.TimeSlotAssociations.Add(new ReservationTimeSlotAssociation()
                {
                    ReservationId = existing.Id,
                    TimeSlotId = newSlotId,
                    TakenTimeSlotSeats = timeSlotsTakeOneSeat.GetValueOrDefault(newSlotId, false) ? 1 : model.Seats
                });
                this.ClearTimeSlotCache(newSlotId);
            }

            // Remove associations for activities that are no longer selected
            foreach (var existingAssociation in existing.AssociatedTimeSlots)
            {
                // PubQuiz: Don't remove quiz associations, quiz cannot be modified this way
                if (existingAssociation.ActivityId is PubQuizSoloActivityId or PubQuizTeamsActivityId)
                    continue;

                if (!model.SelectedTimeSlotIds.ContainsKey(existingAssociation.ActivityId))
                {
                    activityIdsToRemoveSlotsFor.Add(existingAssociation.ActivityId);
                    this.ClearTimeSlotCache(existingAssociation.Id);
                }
            }

            existing.AssociatedTimeSlots.RemoveAll(x => activityIdsToRemoveSlotsFor.Contains(x.ActivityId));

            // Update other modifiable fields
            // existing.SleepOver = model.SleepOver;

            try
            {
                await _dbContext.SaveChangesAsync();
            }
            catch (DbUpdateException e)
            {
                _logger.LogError(e, "Error saving reservation.");

                return ReservationAttemptResult.Error("Database update exception.");
            }

            return ReservationAttemptResult.Updated;
        }
        finally
        {
            if (!force) _slotCountLock.Release();
        }
    }

    public async Task<ReservationAttemptResult> MakeReservation(ReservationModel model, bool force = false,
        bool mustConfirm = true, string? updateToken = null)
    {
        var originalMail = model.Email;
        model.Email = model.Email.ToLowerInvariant();

        if (!_seatsOptions.Value.ReservationsWithoutActivitiesAllowed
            && !model.SelectedTimeSlotIds.Any(x => x.Value.HasValue))
            return ReservationAttemptResult.NoActivityChosen;

        var collisions = await this.CheckCollisions(model);
        if (collisions != null)
        {
            return new ReservationAttemptResult(ReservationAttemptResultCode.TimeslotCollision,
                this.ToModel(collisions.Value.Item1, 0),
                this.ToModel(collisions.Value.Item2, 0));
        }

        var existing = await _dbContext.Reservations.Where(r => r.Email == model.Email)
            .Include(r => r.AssociatedTimeSlots)
            .ThenInclude(ts => ts.CollidesWith)
            .ThenInclude(ts => ts.Activity)
            .FirstOrDefaultAsync();

        if (existing is { Confirmed: true, Cancelled: false })
        {
            if (force || existing.ManagementToken == updateToken)
                return await this.UpdateReservation(model, existing, force);

            return ReservationAttemptResult.EmailTaken;
        }

        ReservationEntity? entity;

        if (!force)
        {
            // Check unreservable slots
            var selectedSlotIds = model.SelectedTimeSlotIds
                .Where(x => x.Value.HasValue)
                .Select(x => x.Value!.Value)
                .ToList();

            if (await this.GetTimeSlotIfUnreservableNow(selectedSlotIds) is { } unreservable)
                return new ReservationAttemptResult(ReservationAttemptResultCode.TimeslotNotReservable,
                    this.ToModel(unreservable, 0));
        }

        if (!force && !await _slotCountLock.WaitAsync(_seatsOptions.Value.LockTimeoutMs))
            return ReservationAttemptResult.Timeout;
        _logger.LogTrace("Lock start: {Email}", model.Email);
        try
        {
            if (!force)
            {
                if (!await this.CheckSeatsLeftForReservation(existing, model.Seats))
                    return ReservationAttemptResult.NoSeatsLeft;

                foreach (var selection in model.SelectedTimeSlotIds)
                {
                    // PubQuiz has custom logic
                    if (selection is { Key: PubQuizSoloActivityId or PubQuizTeamsActivityId } or
                        { Value: PubQuizSoloTimeSlotId or PubQuizTeamsTimeSlotId })
                        continue;

                    if (selection.Value != null &&
                        !await this.CheckSlotSeatsLeftForReservation(selection.Value.Value, existing, model.Seats))
                        return await this.GetTimeslotErrorResult(selection.Value.Value);
                }

                // PubQuiz:
                if (model.WantsPubQuiz)
                {
                    var quizSolo = model.PubQuizReserveSolo;
                    var quizSlotId = quizSolo
                        ? PubQuizSoloTimeSlotId
                        : PubQuizTeamsTimeSlotId;
                    var quizActivityId = quizSolo
                        ? PubQuizSoloActivityId
                        : PubQuizTeamsActivityId;

                    if (!await this.CheckSlotSeatsLeftForReservation(quizSlotId, existing, model.Seats))
                        return await GetTimeslotErrorResult(quizSlotId);

                    model.SelectedTimeSlotIds[quizActivityId] = quizSlotId;

                    if (!quizSolo)
                        model.PubQuizSeats ??= _seatsOptions.Value.MinPubQuizTeamSize;
                }
                else
                {
                    model.PubQuizSeats = 0;
                }
            }

            using var rng = RandomNumberGenerator.Create();
            var array = new byte[16];
            rng.GetNonZeroBytes(array);
            var token = Convert.ToHexString(array);

            if (existing != null)
                _dbContext.Remove(existing);

            var selectedTimeSlotIdsOnly = model.SelectedTimeSlotIds
                .Where(kv => kv.Value.HasValue)
                .Select(kv => kv.Value!.Value)
                .ToList();

            var timeSlotsTakeOneSeat = await _dbContext.TimeSlots
                .Where(x => selectedTimeSlotIdsOnly.Contains(x.Id))
                .Select(x => new { x.Id, x.AlwaysConsumeOnePerReservation })
                .ToDictionaryAsync(x => x.Id, x => x.AlwaysConsumeOnePerReservation);

            var associatedTimeSlots = new List<ReservationTimeSlotAssociation>();
            foreach (var selection in selectedTimeSlotIdsOnly)
            {
                associatedTimeSlots.Add(new ReservationTimeSlotAssociation()
                {
                    TimeSlotId = selection,
                    TakenTimeSlotSeats = timeSlotsTakeOneSeat.GetValueOrDefault(selection, false) ? 1 : model.Seats
                });
            }

            entity = new ReservationEntity()
            {
                ManagementToken = token,
                Email = model.Email,
                MadeOn = SystemClock.Instance.GetCurrentInstant(),
                Seats = model.Seats,
                ConfirmedOn = mustConfirm ? null : SystemClock.Instance.GetCurrentInstant(),

                // SleepOver = model.SleepOver,
                PubQuizTeamName = model.PubQuizTeamName,
                PubQuizSeats = model.PubQuizReserveSolo
                    ? 1
                    : (model.PubQuizSeats ?? _seatsOptions.Value.MinPubQuizTeamSize),

                TimeSlotAssociations = associatedTimeSlots
            };

            _dbContext.Add(entity);
            try
            {
                await _dbContext.SaveChangesAsync();
                _cache.Remove(SeatsLeftCacheKey);
                foreach (var associatedTimeSlot in associatedTimeSlots)
                {
                    this.ClearTimeSlotCache(associatedTimeSlot.TimeSlotId);
                }
            }
            catch (DbUpdateException e)
            {
                _logger.LogError(e, "Error saving reservation.");

                return ReservationAttemptResult.Error("Database update exception.");
            }
        }
        finally
        {
            if (!force) _slotCountLock.Release();
            _logger.LogTrace("Lock end (if not force): {Email}", model.Email);
        }

        if (existing is { Cancelled: false })
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

            return ReservationAttemptResult.MustConfirm;
        }
        else
        {
            await _emailService.SendDoneMail(originalMail,
                entity.Seats,
                _linkService.MakeCancelLink(originalMail, entity.ManagementToken),
                _linkService.MakeEditLink(originalMail, entity.ManagementToken),
                this.GetEmailExtras(entity));

            return ReservationAttemptResult.Confirmed;
        }
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

        if (!force && !await _slotCountLock.WaitAsync(_seatsOptions.Value.LockTimeoutMs))
            return ReservationCompletionResult.Timeout;

        try
        {
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
                foreach (var slot in reservation.AssociatedTimeSlots)
                {
                    this.ClearTimeSlotCache(slot.Id);
                }

                await _emailService.SendDoneMail(originalMail, reservation.Seats,
                    _linkService.MakeCancelLink(originalMail, reservation.ManagementToken),
                    _linkService.MakeEditLink(originalMail, reservation.ManagementToken),
                    this.GetEmailExtras(reservation));
            }
            catch (DbUpdateException e)
            {
                _logger.LogError(e, "Error completing reservation.");

                return ReservationCompletionResult.Error("Database update exception");
            }

            return ReservationCompletionResult.Confirmed;
        }
        finally
        {
            if (!force) _slotCountLock.Release();
        }
    }

    public async Task<(TimeSlotEntity, TimeSlotEntity)?> CheckCollisions(ReservationModel model)
    {
        var selectedSlotIds =
            model.SelectedTimeSlotIds.Values.Where(id => id.HasValue).Select(id => id!.Value).ToList();
        if (selectedSlotIds.Count == 0)
            return null;

        if (model.WantsPubQuiz)
        {
            selectedSlotIds.Add(model.PubQuizReserveSolo ? PubQuizSoloTimeSlotId : PubQuizTeamsTimeSlotId);
        }

        var selectedSlots = await _dbContext.TimeSlots
            .Where(ts => selectedSlotIds.Contains(ts.Id))
            .Include(ts => ts.CollidesWith)
            .Include(ts => ts.Activity)
            .ToListAsync();

        foreach (var slot in selectedSlots)
        {
            foreach (var colliding in slot.CollidesWith)
            {
                if (selectedSlotIds.Contains(colliding.Id))
                    return (slot, colliding);
            }
        }

        return null;
    }

    public async Task<ReservationCompletionResult> CancelReservation(ReservationEntity reservation)
    {
        if (reservation.Cancelled)
            return ReservationCompletionResult.AlreadyConfirmed;

        reservation.CancelledOn = SystemClock.Instance.GetCurrentInstant();
        try
        {
            await _dbContext.SaveChangesAsync();
            _cache.Remove(SeatsLeftCacheKey);
            foreach (var slot in reservation.AssociatedTimeSlots)
            {
                this.ClearTimeSlotCache(slot.Id);
            }

            await _emailService.SendCancelledEmail(reservation.Email, reservation.Seats, reservation.MadeOn);
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

    public async Task<ReservationCompletionResult> CancelReservation(string email, string? token)
    {
        email = email.ToLowerInvariant();

        var reservation = await _dbContext.Reservations.Where(r => r.Email == email)
            .Include(reservationEntity => reservationEntity.AssociatedTimeSlots)
            .FirstOrDefaultAsync();

        if (reservation == null)
            return ReservationCompletionResult.NotFound;

        if (token != null && reservation.ManagementToken != token)
            return ReservationCompletionResult.InvalidToken;

        return await this.CancelReservation(reservation);
    }

    public async Task<ReservationEntity?> GetReservationDetails(string email, string? token = null)
    {
        email = email.ToLowerInvariant();

        IQueryable<ReservationEntity> query = _dbContext.Reservations
            .Include(r => r.AssociatedTimeSlots);

        if (token != null)
        {
            query = query.Where(r => r.ManagementToken == token && r.ConfirmedOn != null && r.CancelledOn == null);
        }

        var result = await query.FirstOrDefaultAsync(r => r.Email == email);
        return result?.Cancelled == true ? null : result;
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
            .ThenInclude(ts => ts.Activity)
            .AsAsyncEnumerable();
    }

    public async Task<string> MakeConfirmedReservationsJson()
    {
        var result = new List<object>();

        await foreach (var reservation in this.GetConfirmedReservations())
        {
            var resultObj = new
            {
                reservation.Email,
                reservation.Seats,
                reservation.SleepOver,
                reservation.HasPubQuizTeam,
                reservation.PubQuizTeamName,
                PubQuizSeats = (reservation.HasPubQuizTeam || reservation.PubQuizSeats == 1)
                    ? reservation.PubQuizSeats
                    : 0,
                Slots = reservation.AssociatedTimeSlots.Select(ts => new
                {
                    ts.Activity.Name,
                    Start = ts.Start.InZone(_zone).ToString("yyyy-MM-dd HH:mm", null),
                    End = ts.End.InZone(_zone).ToString("yyyy-MM-dd HH:mm", null)
                })
            };

            result.Add(resultObj);
        }

        return JsonSerializer.Serialize(result, JsonSerializerOptions.Default);
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

    private void ClearTimeSlotCache(int timeSlotId)
    {
        var cacheKey = string.Format(TimeSlotSeatsLeftCacheKey, timeSlotId);
        _cache.Remove(cacheKey);
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

    private int GetSeatsCountedInPreviousUnfinishedReservation(ReservationEntity? entity)
    {
        if (entity is null or { Cancelled: true })
            return 0;

        return SystemClock.Instance.GetCurrentInstant() - entity.MadeOn <
               Duration.FromMinutes(_seatsOptions.Value.UnconfirmedValidMinutes)
            ? entity.Seats
            : 0;
    }

    private int GetSlotSeatsCountedInPreviousUnfinishedReservation(TimeSlotEntity? timeSlot, ReservationEntity? entity)
    {
        if (entity is null or { Cancelled: true })
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
        var left = await this.GetSeatsLeft() + this.GetSeatsCountedInPreviousUnfinishedReservation(reservation);

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

        var left = await this.GetSlotSeatsLeft(timeSlot.Id)
                   + this.GetSlotSeatsCountedInPreviousUnfinishedReservation(timeSlot, reservation);

        _logger.LogTrace("CheckSlotSeatsLeftForReservation / {Email} / Left: {Left} / CountedInRes: {CIR}",
            reservation?.Email, left, this.GetSlotSeatsCountedInPreviousUnfinishedReservation(timeSlot, reservation));

        var seatsToConsume = timeSlot.AlwaysConsumeOnePerReservation ? 1 : seats;

        return left - seatsToConsume >= 0;
    }

    public async Task<List<SlottedActivity>> GetSlottedActivities(bool showStartedSlots = false)
    {
        var activities = await _dbContext.Activities
            .OrderBy(x => x.Id)
            .Select(x => new SlottedActivity()
            {
                Id = x.Id,
                Name = x.Name,
                TimeSlots = new List<TimeSlot>(),
                Description = x.Description
            }).ToListAsync();

        foreach (var activity in activities)
        {
            var slots = await this.GetTimeslotsForActivity(activity.Id, showStartedSlots);
            activity.TimeSlots.AddRange(slots);
        }

        return activities;
    }

    private IEnumerable<TimeSlot> GetEmailExtras(ReservationEntity reservationEntity)
    {
        var extras = reservationEntity.AssociatedTimeSlots
            .Select(x => this.ToModel(x, 0));
        return extras;
    }

    public async Task<TimeSlotWithReservations?> GetTimeSlotWithReservations(int timeslotId)
    {
        var slotEntity = await _dbContext.TimeSlots
            .Where(x => x.Id == timeslotId)
            .Include(x => x.Activity)
            .Include(x => x.AssociatedReservations)
            .FirstOrDefaultAsync();

        if (slotEntity == null)
            return null;

        return this.ToModel(slotEntity,
            this.CalculateTimeslotAvailableSeats(slotEntity, SystemClock.Instance.GetCurrentInstant()), true);
    }

    private TimeSlotWithReservations ToModel(TimeSlotEntity slotEntity, int availableSeats,
        bool includeReservations = false)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var reservable = true;
        ZonedDateTime? reserveBound = null;

        if (slotEntity.ReserveBefore is not null && now > slotEntity.ReserveBefore)
        {
            reservable = false;
            reserveBound = slotEntity.ReserveBefore?.InZone(_zone);
        }
        else if (slotEntity.ReserveAfter is not null && now < slotEntity.ReserveAfter)
        {
            reservable = false;
            reserveBound = slotEntity.ReserveAfter?.InZone(_zone);
        }

        return new TimeSlotWithReservations()
        {
            Id = slotEntity.Id,
            ActivityId = slotEntity.ActivityId,
            Start = slotEntity.Start.InZone(_zone),
            End = slotEntity.End.InZone(_zone),
            ActivityName = slotEntity.Activity.Name,
            TotalSeats = slotEntity.TotalSeats,
            AvailableSeats = availableSeats,
            Note = slotEntity.Note,
            AlwaysConsumeOnePerReservation = slotEntity.AlwaysConsumeOnePerReservation,
            IsReservable = reservable,
            ReserveDateBound = reserveBound,
            Reservations = includeReservations
                ? slotEntity
                    .AssociatedReservations
                    .Select(x => new ReservationOverview()
                    {
                        Email = x.Email, Seats = x.Seats, SleepOver = x.SleepOver, Cancelled = x.Cancelled,
                        Confirmed = x.Confirmed, MadeOn = x.MadeOn.InZone(_zone),
                        PubQuizTeamName = x.PubQuizTeamName, PubQuizSeats = x.PubQuizSeats
                    }).ToList()
                : ImmutableList.Create<ReservationOverview>()
        };
    }

    private int CalculateTimeslotAvailableSeats(TimeSlotEntity slotEntity, Instant at)
    {
        var unconfirmedValidMinutes = Duration.FromMinutes(_seatsOptions.Value.UnconfirmedValidMinutes);

        var availableSeats = slotEntity.TotalSeats
                             - slotEntity.AssociatedReservations
                                 .Where(r => r.CancelledOn == null &&
                                             (r.ConfirmedOn != null || (at - r.MadeOn) < unconfirmedValidMinutes))
                                 .Sum(x => slotEntity.AlwaysConsumeOnePerReservation ? 1 : x.Seats);

        return availableSeats;
    }

    public async Task<ReservationCompletionResult> CancelReservationIfEmpty(string email)
    {
        email = email.ToLowerInvariant();

        var reservation = await _dbContext.Reservations.Where(r => r.Email == email)
            .Include(reservationEntity => reservationEntity.AssociatedTimeSlots)
            .FirstOrDefaultAsync();

        if (reservation == null)
            return ReservationCompletionResult.NotFound;

        if (reservation.AssociatedTimeSlots.Count != 0)
            return ReservationCompletionResult.Confirmed;

        return await this.CancelReservation(reservation);
    }

    public async Task<List<TimeSlot>> GetTimeslotsForActivity(int slottedActivityId, bool showStarted = false)
    {
        var now = SystemClock.Instance.GetCurrentInstant();

        var slotsQuery = _dbContext.TimeSlots
            .Where(x => x.ActivityId == slottedActivityId);

        if (!showStarted)
        {
            slotsQuery = slotsQuery.Where(x => x.Start > now);
        }

        var slots = await slotsQuery.Include(x => x.Activity)
            .Include(x => x.AssociatedReservations)
            .ToListAsync();

        var ret = new List<TimeSlot>();
        foreach (var slotEntity in slots)
        {
            var slotModel = this.ToModel(slotEntity,
                this.CalculateTimeslotAvailableSeats(slotEntity, now));
            ret.Add(slotModel);
        }

        return ret;
    }

    public async Task<(bool Teams, bool Solo)> GetPubQuizAvailability()
    {
        var teamsSlots = await this.GetTimeslotsForActivity(PubQuizTeamsActivityId);
        var soloSlots = await this.GetTimeslotsForActivity(PubQuizSoloActivityId);

        return (teamsSlots.Count > 0 && teamsSlots[0].AvailableSeats > 0,
            soloSlots.Count > 0 && soloSlots[0].AvailableSeats > 0);
    }
}
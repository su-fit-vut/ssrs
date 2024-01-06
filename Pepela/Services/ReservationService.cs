// ReservationService.cs
// Author: Ondřej Ondryáš

using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NodaTime;
using Pepela.Configuration;
using Pepela.Data;
using Pepela.Models;

namespace Pepela.Services;

public class ReservationService
{
    private readonly AppDbContext _dbContext;
    private readonly EmailService _emailService;
    private readonly LinkService _linkService;
    private readonly IOptions<SeatsOptions> _seatsOptions;
    private readonly ILogger<ReservationService> _logger;

    public ReservationService(AppDbContext dbContext, EmailService emailService, LinkService linkService,
        IOptions<SeatsOptions> seatsOptions, ILogger<ReservationService> logger)
    {
        _dbContext = dbContext;
        _emailService = emailService;
        _linkService = linkService;
        _seatsOptions = seatsOptions;
        _logger = logger;
    }

    public async Task<ReservationAttemptResult> MakeReservation(ReservationModel model, bool force = false,
        bool mustConfirm = true)
    {
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
            await _emailService.SendConfirmationMail(entity.Email,
                _linkService.MakeConfirmLink(entity.Email, entity.ManagementToken),
                _linkService.MakeCancelLink(entity.Email, entity.ManagementToken));
        }

        return ReservationAttemptResult.MustConfirm;
    }

    public async Task<ReservationCompletionResult> ConfirmReservation(string email, string? token, bool force = false)
    {
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
            await _emailService.SendDoneMail(reservation.Email, reservation.Seats,
                _linkService.MakeCancelLink(reservation.Email, reservation.ManagementToken));
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
            await _emailService.SendCancelledEmail(reservation.Email, reservation.Seats, reservation.MadeOn);
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

    public async Task<int> GetSeatsLeft()
    {
        var total = await this.GetTotalSeats();

        var now = SystemClock.Instance.GetCurrentInstant();
        var unconfirmedValidMinutes = Duration.FromMinutes(_seatsOptions.Value.UnconfirmedValidMinutes);

        var taken = await _dbContext.Reservations.Where(r => r.CancelledOn == null &&
                                        (r.ConfirmedOn != null || (now - r.MadeOn) < unconfirmedValidMinutes))
                                    .SumAsync(r => r.Seats);

        return int.Max(0, total - taken);
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
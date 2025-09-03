// MakeReservationResult.cs
// Author: Ondřej Ondryáš

namespace Pepela.Models;

public enum ReservationAttemptResultCode
{
    MustConfirm,
    NoSeatsLeft,
    TimeslotError,
    TimeslotNotReservable,
    EmailTaken,
    TimeslotCollision,
    NoActivityChosen,
    Updated,
    Confirmed,
    Timeout,
    Error
}

public enum ReservationCompletionResultCode
{
    Confirmed,
    AlreadyConfirmed,
    NoSeatsLeft,
    TimeslotError,
    NotFound,
    InvalidToken,
    Timeout,
    Error
}

public record struct ReservationAttemptResult(
    ReservationAttemptResultCode Code,
    TimeSlot? CollidingTimeSlot = null,
    TimeSlot? CollidingTimeSlotRight = null,
    string? ErrorMessage = null)
{
    public static readonly ReservationAttemptResult MustConfirm = new(ReservationAttemptResultCode.MustConfirm);
    public static readonly ReservationAttemptResult NoSeatsLeft = new(ReservationAttemptResultCode.NoSeatsLeft);
    public static readonly ReservationAttemptResult EmailTaken = new(ReservationAttemptResultCode.EmailTaken);
    public static readonly ReservationAttemptResult NoActivityChosen = new(ReservationAttemptResultCode.NoActivityChosen);
    public static readonly ReservationAttemptResult Updated = new(ReservationAttemptResultCode.Updated);
    public static readonly ReservationAttemptResult Confirmed = new(ReservationAttemptResultCode.Confirmed);
    public static readonly ReservationAttemptResult Timeout = new(ReservationAttemptResultCode.Timeout);

    public static ReservationAttemptResult Error(string error)
        => new(ReservationAttemptResultCode.Error, null, null, error);
}

public record struct ReservationCompletionResult(
    ReservationCompletionResultCode Code,
    TimeSlot? CollidingTimeSlot,
    string? ErrorMessage = null)
{
    public static readonly ReservationCompletionResult Confirmed
        = new(ReservationCompletionResultCode.Confirmed, null);

    public static readonly ReservationCompletionResult AlreadyConfirmed
        = new(ReservationCompletionResultCode.AlreadyConfirmed, null);

    public static readonly ReservationCompletionResult NoSeatsLeft
        = new(ReservationCompletionResultCode.NoSeatsLeft, null);

    public static readonly ReservationCompletionResult NotFound
        = new(ReservationCompletionResultCode.NotFound, null);

    public static readonly ReservationCompletionResult InvalidToken
        = new(ReservationCompletionResultCode.InvalidToken, null);
    
    public static readonly ReservationCompletionResult Timeout
        = new(ReservationCompletionResultCode.Timeout, null);

    public static ReservationCompletionResult Error(string error)
        => new(ReservationCompletionResultCode.Error, null, error);
}
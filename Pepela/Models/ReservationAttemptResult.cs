// MakeReservationResult.cs
// Author: Ondřej Ondryáš

namespace Pepela.Models;

public enum ReservationAttemptResultCode
{
    MustConfirm,
    NoSeatsLeft,
    TimeslotError,
    EmailTaken,
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
    Error
}

public record struct ReservationAttemptResult(
    ReservationAttemptResultCode Code,
    TimeSlot? CollidingTimeSlot,
    string? ErrorMessage = null)
{
    public static readonly ReservationAttemptResult MustConfirm = new(ReservationAttemptResultCode.MustConfirm, null);
    public static readonly ReservationAttemptResult NoSeatsLeft = new(ReservationAttemptResultCode.NoSeatsLeft, null);
    public static readonly ReservationAttemptResult EmailTaken = new(ReservationAttemptResultCode.EmailTaken, null);

    public static ReservationAttemptResult Error(string error)
        => new(ReservationAttemptResultCode.Error, null, error);
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

    public static ReservationCompletionResult Error(string error)
        => new(ReservationCompletionResultCode.Error, null, error);
}
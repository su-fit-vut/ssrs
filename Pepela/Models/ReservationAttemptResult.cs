// MakeReservationResult.cs
// Author: Ondřej Ondryáš

namespace Pepela.Models;

public enum ReservationAttemptResult
{
    MustConfirm,
    NoSeatsLeft,
    EmailTaken,
    Error
}

public enum ReservationCompletionResult
{
    Confirmed,
    AlreadyConfirmed,
    NoSeatsLeft,
    NotFound,
    InvalidToken,
    Error
}

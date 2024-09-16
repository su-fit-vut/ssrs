// MakeReservationResult.cs
// Author: Ondřej Ondryáš

namespace Pepela.Models;

public enum ReservationAttemptResult
{
    MustConfirm,
    NoSeatsLeft,
    NoPubQuizTeamsLeft,
    TimeslotError,
    EmailTaken,
    Error
}

public enum ReservationCompletionResult
{
    Confirmed,
    AlreadyConfirmed,
    NoSeatsLeft,
    NoPubQuizTeamsLeft,
    TimeslotError,
    NotFound,
    InvalidToken,
    Error
}

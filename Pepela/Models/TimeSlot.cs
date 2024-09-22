// TimeSlot.cs
// Author: Ondřej Ondryáš

using NodaTime;

namespace Pepela.Models;

public record TimeSlot
{
    public required int Id { get; init; }
    public required ZonedDateTime Start { get; init; }
    public required ZonedDateTime End { get; init; }
    public required int ActivityId { get; init; }
    public required string ActivityName { get; init; }
    public required int TotalSeats { get; init; }
    public required int AvailableSeats { get; init; }
    public string? Note { get; init; }

    public bool IsAvailable => AvailableSeats > 0;
    public bool AlwaysConsumeOnePerReservation { get; init; }
}

public record TimeSlotForJob
{
    public string ActivityName { get; init; }
    public string? Note { get; init; }
    public long Start { get; init; }
    public long End { get; init; }

    public TimeSlotForJob()
    {
        ActivityName = string.Empty;
    }

    public TimeSlotForJob(TimeSlot slot)
    {
        ActivityName = slot.ActivityName;
        Note = slot.Note;
        Start = slot.Start.ToInstant().ToUnixTimeSeconds();
        End = slot.End.ToInstant().ToUnixTimeSeconds();
    }
}
using System.ComponentModel.DataAnnotations;

namespace Pepela.Data;

public class ReservationTimeSlotAssociation
{
    public int ReservationId { get; set; }
    public ReservationEntity Reservation { get; set; } = null!;
    
    public int TimeSlotId { get; set; }
    public TimeSlotEntity TimeSlot { get; set; } = null!;
    
    public int TakenTimeSlotSeats { get; set; }
}
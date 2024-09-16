// SlottedActivityEntity.cs
// Author: Ondřej Ondryáš

using System.ComponentModel.DataAnnotations;

namespace Pepela.Data;

public class SlottedActivityEntity
{
    [Key] public int Id { get; set; }
    [Required] [MaxLength(64)] public string Name { get; set; } = null!;
    public List<TimeSlotEntity> TimeSlots { get; set; } = null!;
}
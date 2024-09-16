// SlottedActivity.cs
// Author: Ondřej Ondryáš

namespace Pepela.Models;

public record SlottedActivity
{
    public required int Id { get; init; }
    public required string Name { get; init; }
}
﻿// SeatsOptions.cs
// Author: Ondřej Ondryáš

namespace Pepela.Configuration;

public class SeatsOptions
{
    public int TotalSeats { get; set; }
    public int MaximumPerEmail { get; set; }
    public int UnconfirmedValidMinutes { get; set; } = 10;

    public int MinPubQuizTeamSize { get; set; } = 2;
    public int MaxPubQuizTeamSize { get; set; } = 8;
}
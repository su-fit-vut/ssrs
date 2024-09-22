// SendReminderEmailJob.cs
// Author: Ondřej Ondryáš

using System.Text.Json;
using NodaTime;
using Pepela.Models;
using Pepela.Services;
using Quartz;

namespace Pepela.Jobs;

public class SendReminderEmailJob : IJob
{
    private readonly EmailService _emailService;
    private readonly DateTimeZone _zone;

    public SendReminderEmailJob(EmailService emailService)
    {
        _emailService = emailService;
        _zone = DateTimeZoneProviders.Tzdb["Europe/Prague"];
    }

    public async Task Execute(IJobExecutionContext context)
    {
        try
        {
            await Task.Delay(Random.Shared.Next(2000), context.CancellationToken);
        }
        catch
        {
            return;
        }

        var data = context.JobDetail.JobDataMap;
        var to = data.GetString("email") ?? throw new JobExecutionException();
        var seats = data.GetIntValue("seats");
        var cancelLink = data.GetString("link") ?? throw new JobExecutionException();
        var timeSlotsJson = data.GetString("timeSlots");
        IEnumerable<TimeSlot>? timeSlots = null;
        if (!string.IsNullOrEmpty(timeSlotsJson))
        {
            timeSlots = JsonSerializer.Deserialize<List<TimeSlotForJob>>(timeSlotsJson)
                ?.Select(x => new TimeSlot()
                {
                    ActivityName = x.ActivityName,
                    Note = x.Note,
                    Start = Instant.FromUnixTimeSeconds(x.Start).InZone(_zone),
                    End = Instant.FromUnixTimeSeconds(x.End).InZone(_zone),
                    TotalSeats = 0,
                    AvailableSeats = 0,
                    ActivityId = 0,
                    Id = 0,
                    AlwaysConsumeOnePerReservation = false
                });
        }

        await _emailService.SendReminderEmail(to, seats, cancelLink, timeSlots);
    }
}
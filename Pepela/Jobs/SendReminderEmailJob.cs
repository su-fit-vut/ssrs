// SendReminderEmailJob.cs
// Author: Ondřej Ondryáš

using Pepela.Services;
using Quartz;

namespace Pepela.Jobs;

public class SendReminderEmailJob : IJob
{
    private readonly EmailService _emailService;

    public SendReminderEmailJob(EmailService emailService)
    {
        _emailService = emailService;
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

        await _emailService.SendReminderEmail(to, seats, cancelLink);
    }
}
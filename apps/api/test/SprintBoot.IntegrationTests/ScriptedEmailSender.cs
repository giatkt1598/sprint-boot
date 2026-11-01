using System.Net.Mail;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Emailing;
using Volo.Abp.MultiTenancy;

namespace SprintBoot.IntegrationTests;

/// <summary>Accepts every message except those to a bounce address, so delivery failures are repeatable.</summary>
public class ScriptedEmailSender(
    ICurrentTenant currentTenant,
    IEmailSenderConfiguration configuration,
    IBackgroundJobManager backgroundJobManager)
    : NullEmailSender(currentTenant, configuration, backgroundJobManager)
{
    public const string BouncePrefix = "bounce-";

    protected override Task SendEmailAsync(MailMessage mail) =>
        mail.To.Any(address => address.Address.StartsWith(BouncePrefix, StringComparison.Ordinal))
            ? throw new SmtpException("The test mailbox refuses this address.")
            : Task.CompletedTask;
}

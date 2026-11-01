using System.Net;
using Microsoft.Extensions.Configuration;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Emailing;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Identity;
using Volo.Abp.Uow;

namespace SprintBoot.Identity;

public class PasswordResetEmailHandler(
    IdentityUserManager userManager,
    IEmailSender emailSender,
    IConfiguration configuration)
    : IDistributedEventHandler<PasswordResetRequestedEto>, ITransientDependency
{
    [UnitOfWork]
    public virtual async Task HandleEventAsync(PasswordResetRequestedEto eventData)
    {
        var user = await userManager.FindByIdAsync(eventData.UserId.ToString());
        if (user is null)
        {
            return;
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var baseUrl = SpaUrls.GetBaseUrl(configuration);
        var link = $"{baseUrl}/reset-password?userId={user.Id}&token={WebUtility.UrlEncode(token)}";
        var texts = EmailTexts.Load(eventData.Language);
        var body = $"<p>{WebUtility.HtmlEncode(texts["resetBody"])}</p>" +
            $"<p><a href=\"{WebUtility.HtmlEncode(link)}\">{WebUtility.HtmlEncode(texts["resetAction"])}</a></p>";

        await emailSender.SendAsync(user.Email, texts["resetSubject"], body);
    }
}

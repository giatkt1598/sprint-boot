using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenIddict.Server.AspNetCore;
using OpenIddict.Validation.AspNetCore;
using Volo.Abp.Application;
using Volo.Abp.AspNetCore.Mvc.AntiForgery;
using Volo.Abp.Domain;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Identity;
using Volo.Abp.Identity.AspNetCore;
using Volo.Abp.MailKit;
using Volo.Abp.Modularity;
using Volo.Abp.OpenIddict;
using Volo.Abp.PermissionManagement.Identity;

namespace SprintBoot.Identity;

[DependsOn(
    typeof(IdentityContractsModule),
    typeof(AbpDddApplicationModule),
    typeof(AbpDddDomainModule),
    typeof(AbpEntityFrameworkCoreModule),
    typeof(AbpIdentityDomainModule),
    typeof(AbpIdentityAspNetCoreModule),
    typeof(AbpOpenIddictAspNetCoreModule),
    typeof(AbpMailKitModule),
    typeof(AbpPermissionManagementDomainIdentityModule))]
public class IdentityModule : AbpModule
{
    /// <summary>The session cookie only exists for the authorize flow; APIs use bearer tokens.</summary>
    public const string SessionCookiePath = "/connect";

    public override void PreConfigureServices(ServiceConfigurationContext context)
    {

        PreConfigure<OpenIddictBuilder>(builder => builder.AddValidation(options =>
        {
            options.AddAudiences(SpaClientDataSeedContributor.ApiScope);
            options.UseLocalServer();
            options.UseAspNetCore();
        }));

        PreConfigure<OpenIddictServerBuilder>(builder =>
        {
            builder.RegisterScopes(SpaClientDataSeedContributor.ApiScope);
            builder.SetRevocationEndpointUris("connect/revocation");
            builder.SetAccessTokenLifetime(TimeSpan.FromMinutes(15));
            builder.SetRefreshTokenLifetime(TimeSpan.FromDays(14));
        });

    }

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "sb.session";
            options.Cookie.Path = SessionCookiePath;
            options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
            options.Cookie.HttpOnly = true;
            options.LoginPath = "/login";

            options.ForwardDefaultSelector = http =>
                http.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                    ? OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme
                    : null;

            options.Events.OnRedirectToLogin = http =>
            {
                if (http.Request.Path.StartsWithSegments("/api"))
                {
                    http.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                }

                http.Response.Redirect(http.RedirectUri);
                return Task.CompletedTask;
            };
        });


        if (!context.Services.GetConfiguration().GetValue("Auth:RequireHttps", defaultValue: true))
        {
            Configure<OpenIddictServerAspNetCoreOptions>(options => options.DisableTransportSecurityRequirement = true);
        }

        // APIs authenticate by bearer token, so only the anonymous cookie endpoints validate CSRF, explicitly.
        Configure<AbpAntiForgeryOptions>(options =>
        {
            options.AutoValidate = false;
            options.TokenCookie.SameSite = SameSiteMode.Lax;
        });

        Configure<IdentityOptions>(options =>
        {
            options.Lockout.AllowedForNewUsers = true;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        });
    }
}

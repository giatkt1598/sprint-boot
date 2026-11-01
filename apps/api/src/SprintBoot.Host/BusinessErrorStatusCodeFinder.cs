using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Volo.Abp.AspNetCore.ExceptionHandling;
using Volo.Abp.DependencyInjection;
using Volo.Abp;

namespace SprintBoot.Host;

/// <summary>A broken business rule is a bad request; ABP's default 403 would read as a permission failure.</summary>
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(IHttpExceptionStatusCodeFinder), typeof(DefaultHttpExceptionStatusCodeFinder))]
public class BusinessErrorStatusCodeFinder(IOptions<AbpExceptionHttpStatusCodeOptions> options)
    : DefaultHttpExceptionStatusCodeFinder(options)
{
    public override System.Net.HttpStatusCode GetStatusCode(HttpContext httpContext, Exception exception) =>
        exception is IBusinessException
            ? System.Net.HttpStatusCode.BadRequest
            : base.GetStatusCode(httpContext, exception);
}

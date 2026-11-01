using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Mvc.AntiForgery;

namespace SprintBoot.Identity;

/// <summary>
/// Login uses the ASP.NET Core CSRF filter: ABP filters skip requests without an auth
/// cookie, which would leave login CSRF open.
/// </summary>
[Route("api/v1/auth")]
public class AuthController(IAuthAppService authAppService, IAbpAntiForgeryManager antiForgeryManager) : AbpController
{
    [HttpGet("csrf")]
    [AllowAnonymous]
    public void GetCsrf() => antiForgeryManager.SetCookie();

    [HttpPost("login")]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public Task LoginAsync([FromBody] LoginInput input) => authAppService.LoginAsync(input);

    [HttpGet("me")]
    [Authorize]
    public Task<CurrentUserDto> GetMeAsync() => authAppService.GetMeAsync();
}

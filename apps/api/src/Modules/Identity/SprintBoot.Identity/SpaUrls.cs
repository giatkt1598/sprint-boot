using Microsoft.Extensions.Configuration;

namespace SprintBoot.Identity;

public static class SpaUrls
{
    public static string GetBaseUrl(IConfiguration configuration)
    {
        var value = configuration["Auth:SpaBaseUrl"];
        return string.IsNullOrEmpty(value) ? "http://localhost:5173" : value.TrimEnd('/');
    }
}

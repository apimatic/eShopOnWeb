using System;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>
/// Settings of the Mindbody integration, read from the "Mindbody:" configuration section.
/// </summary>
public class MindbodySettings
{
    public const string SECTION = "Mindbody";

    public string? ApiKey { get; set; }
    public string? SiteId { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }

    /// <summary>Optional override of the API base address; used verbatim when set.</summary>
    public string? BaseUrl { get; set; }

    public static MindbodySettings Load(IConfiguration configuration)
    {
        var section = configuration.GetSection(SECTION);
        return new MindbodySettings
        {
            ApiKey = Read(section, "ApiKey", "MINDBODY_API_KEY"),
            SiteId = Read(section, "SiteId", "MINDBODY_SITE_ID"),
            Username = Read(section, "Username", "MINDBODY_USERNAME"),
            Password = Read(section, "Password", "MINDBODY_PASSWORD"),
            BaseUrl = Read(section, "BaseUrl", null),
        };
    }

    // The configuration section is authoritative; the environment variable only fills a key that is not configured.
    private static string? Read(IConfigurationSection section, string key, string? environmentFallback)
    {
        var value = section[key];
        if (string.IsNullOrWhiteSpace(value) && environmentFallback != null)
        {
            value = Environment.GetEnvironmentVariable(environmentFallback);
        }
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

using System;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>
/// The <c>Mindbody:</c> configuration section. No values are defined in the repository: they come from
/// configuration (user-secrets, environment, ...). As a convenience for sandbox runs, a missing key falls back
/// to the matching <c>MINDBODY_*</c> environment variable.
/// </summary>
public class MindbodySettings
{
    public const string CONFIG_NAME = "Mindbody";

    public string? ApiKey { get; set; }
    public string? SiteId { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }

    /// <summary>Optional override of the API's base address; used verbatim when set.</summary>
    public string? BaseUrl { get; set; }

    public static MindbodySettings Load(IConfiguration configuration)
    {
        var section = configuration.GetSection(CONFIG_NAME);
        return new MindbodySettings
        {
            ApiKey = Pick(section["ApiKey"], "MINDBODY_API_KEY"),
            SiteId = Pick(section["SiteId"], "MINDBODY_SITE_ID"),
            Username = Pick(section["Username"], "MINDBODY_USERNAME"),
            Password = Pick(section["Password"], "MINDBODY_PASSWORD"),
            BaseUrl = string.IsNullOrEmpty(section["BaseUrl"]) ? null : section["BaseUrl"],
        };
    }

    private static string? Pick(string? configured, string environmentVariable) =>
        !string.IsNullOrWhiteSpace(configured) ? configured : Environment.GetEnvironmentVariable(environmentVariable);
}

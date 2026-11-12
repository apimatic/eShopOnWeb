using System;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>Settings bound from the <c>Maxio:</c> configuration section.</summary>
public class MaxioSettings
{
    public const string SECTION = "Maxio";

    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? Environment { get; set; }
    public string? BaseUrl { get; set; }

    /// <summary>Reads and validates the settings; throws at startup when one is missing or blank.</summary>
    public static MaxioSettings Load(IConfiguration configuration)
    {
        var s = configuration.GetSection(SECTION).Get<MaxioSettings>() ?? new MaxioSettings();
        var problems = new System.Collections.Generic.List<string>();
        if (string.IsNullOrWhiteSpace(s.ApiKey)) problems.Add($"{SECTION}:ApiKey");
        if (string.IsNullOrWhiteSpace(s.Environment)) problems.Add($"{SECTION}:Environment");
        else if (!s.Environment.Trim().Equals("US", StringComparison.OrdinalIgnoreCase)
                 && !s.Environment.Trim().Equals("EU", StringComparison.OrdinalIgnoreCase))
            problems.Add($"{SECTION}:Environment (must be US or EU)");
        if (string.IsNullOrWhiteSpace(s.Subdomain)) problems.Add($"{SECTION}:Subdomain");
        if (problems.Count > 0)
            throw new InvalidOperationException("Missing or invalid Maxio settings: " + string.Join(", ", problems));
        return s;
    }

    public bool IsEu => Environment!.Trim().Equals("EU", StringComparison.OrdinalIgnoreCase);
}

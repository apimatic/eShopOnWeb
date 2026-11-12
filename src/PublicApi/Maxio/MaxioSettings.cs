using System;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Settings bound from the "Maxio:" configuration section (user-secrets in development).
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio API key (Maxio:ApiKey).</summary>
    [Required(AllowEmptyStrings = false)]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maxio site subdomain (Maxio:Subdomain).</summary>
    [Required(AllowEmptyStrings = false)]
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>"US" or "EU" (Maxio:Environment).</summary>
    [Required(AllowEmptyStrings = false)]
    public string Environment { get; set; } = string.Empty;

    /// <summary>Optional override; when set it is used verbatim as the API base address (Maxio:BaseUrl).</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Whole-call budget (Maxio:CallBudgetSeconds), optional.</summary>
    public int CallBudgetSeconds { get; set; } = 30;

    public bool IsEu => string.Equals(Environment?.Trim(), "EU", StringComparison.OrdinalIgnoreCase);

    public bool IsValidEnvironment =>
        IsEu || string.Equals(Environment?.Trim(), "US", StringComparison.OrdinalIgnoreCase);
}

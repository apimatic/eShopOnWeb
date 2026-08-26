using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the "Maxio" configuration section.
/// Secrets (ApiKey) are supplied via .NET user-secrets or environment variables, never via appsettings files.
/// </summary>
public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    [Required]
    public string ApiKey { get; set; } = string.Empty;

    [Required]
    public string Subdomain { get; set; } = string.Empty;

    [Required]
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the API base address. When set, it is used verbatim
    /// instead of deriving the address from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string GetApiBaseUrl() =>
        !string.IsNullOrWhiteSpace(BaseUrl)
            ? BaseUrl!.TrimEnd('/')
            : $"https://{Subdomain}.chargify.com";
}

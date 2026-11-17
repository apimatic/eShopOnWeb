using System;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Strongly typed binding of the <c>Finix:</c> configuration section.
/// Values are supplied by the deployment (environment variables / user-secrets); none are hard-coded.
/// </summary>
public class FinixOptions
{
    public const string SectionName = "Finix";

    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string MerchantId { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// Optional override. When set it is used verbatim as the base address of every Finix call.
    /// </summary>
    public string? BaseUrl { get; set; }

    public bool IsLive => string.Equals(Environment, "live", StringComparison.OrdinalIgnoreCase);
}

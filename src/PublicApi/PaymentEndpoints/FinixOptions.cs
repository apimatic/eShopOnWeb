namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Bound from the "Finix" configuration section. Values come from user-secrets
/// (or environment configuration) and are never committed to the repository.
/// </summary>
public sealed class FinixOptions
{
    public const string SectionName = "Finix";

    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string MerchantId { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }
}

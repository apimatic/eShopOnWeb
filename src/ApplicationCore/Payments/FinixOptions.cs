namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Configuration bound from the "Finix" configuration section. Values come from
/// FINIX_USERNAME, FINIX_PASSWORD, FINIX_MERCHANT_ID, FINIX_ENVIRONMENT and FINIX_CURRENCY
/// (or from user-secrets), never from source control.
/// </summary>
public class FinixOptions
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string MerchantId { get; set; } = string.Empty;
    public string Environment { get; set; } = "sandbox";
    public string Currency { get; set; } = "USD";
    public string? BaseUrl { get; set; }
}

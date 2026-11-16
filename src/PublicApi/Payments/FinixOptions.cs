namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Bound from the <c>Finix</c> configuration section. The values are supplied by the
/// deployment (environment variables, user-secrets or a secret store); none is hard-coded.
/// </summary>
public class FinixOptions
{
    public const string SectionName = "Finix";

    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string MerchantId { get; set; } = string.Empty;
    public string Environment { get; set; } = "sandbox";
    public string Currency { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }
}

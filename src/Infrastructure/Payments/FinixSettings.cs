namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Settings bound from the <c>Finix</c> configuration section. Values are supplied by the
/// environment / user-secrets; no credentials are committed to the repository.
/// </summary>
public sealed class FinixSettings
{
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? MerchantId { get; set; }
    public string? Environment { get; set; }
    public string? Currency { get; set; }
    public string? BaseUrl { get; set; }
}

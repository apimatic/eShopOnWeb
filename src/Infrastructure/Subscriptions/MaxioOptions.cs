namespace Microsoft.eShopWeb.Infrastructure.Subscriptions;

/// <summary>
/// Bound from the "Maxio" configuration section. Values must come from configuration
/// (user-secrets in Development, environment variables/secret store elsewhere) — never hard-coded,
/// since the same build runs against different Maxio sites/catalogs per environment.
/// </summary>
public class MaxioOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }
}

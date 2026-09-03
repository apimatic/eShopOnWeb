namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;

    public string Subdomain { get; set; } = string.Empty;

    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional verbatim API base address. When set, used instead of deriving a URL from Subdomain.
    /// </summary>
    public string? BaseUrl { get; set; }
}

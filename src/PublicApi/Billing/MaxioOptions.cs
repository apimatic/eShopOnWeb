namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Settings bound from the "Maxio" configuration section. Values are supplied via
/// user-secrets or environment variables (Maxio__ApiKey, Maxio__Subdomain,
/// Maxio__ProductFamilyHandle, Maxio__BaseUrl) — never from files in this repo.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional override. When set, used verbatim as the API base address instead of
    /// deriving one from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Optional. How subscriptions are collected: automatic, remittance, prepaid or invoice.
    /// Defaults to remittance (billed by invoice) so subscribing works without a card on file;
    /// set to automatic for sites whose products capture payment at signup.
    /// </summary>
    public string? CollectionMethod { get; set; }
}

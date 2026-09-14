namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// The site-level facts this integration needs: the currency plans are priced in, and which billing
/// architecture the site runs — the latter decides which collection method the provider will accept.
/// </summary>
public sealed class MaxioSiteInfo
{
    public MaxioSiteInfo(string? currency, bool relationshipInvoicingEnabled, bool? isTestSite)
    {
        Currency = currency;
        RelationshipInvoicingEnabled = relationshipInvoicingEnabled;
        IsTestSite = isTestSite;
    }

    /// <summary>ISO currency code the site bills in, e.g. <c>USD</c>.</summary>
    public string? Currency { get; }

    /// <summary>True on Relationship Invoicing; false on the legacy Statements architecture.</summary>
    public bool RelationshipInvoicingEnabled { get; }

    public bool? IsTestSite { get; }
}

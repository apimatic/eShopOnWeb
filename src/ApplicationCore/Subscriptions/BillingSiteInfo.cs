namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The billing site settings this build needs in order to behave correctly against whichever
/// site it is pointed at: which currency plans are priced in, which invoicing architecture is in
/// use, and what the site collects payment by default.
/// </summary>
public class BillingSiteInfo
{
    public BillingSiteInfo(
        long id,
        string? name,
        string? subdomain,
        string? currency,
        bool relationshipInvoicingEnabled,
        string? defaultPaymentCollectionMethod,
        bool isTestSite)
    {
        Id = id;
        Name = name;
        Subdomain = subdomain;
        Currency = currency;
        RelationshipInvoicingEnabled = relationshipInvoicingEnabled;
        DefaultPaymentCollectionMethod = defaultPaymentCollectionMethod;
        IsTestSite = isTestSite;
    }

    public long Id { get; }

    public string? Name { get; }

    public string? Subdomain { get; }

    /// <summary>Primary ISO currency code of the site, e.g. <c>USD</c>.</summary>
    public string? Currency { get; }

    /// <summary>
    /// True when the site runs the Relationship Invoicing architecture. This decides which
    /// non-automatic collection method is valid: <c>remittance</c> on RI sites, <c>invoice</c> on
    /// legacy Statements sites.
    /// </summary>
    public bool RelationshipInvoicingEnabled { get; }

    public string? DefaultPaymentCollectionMethod { get; }

    public bool IsTestSite { get; }

    /// <summary>
    /// The collection method to use for a plan that does not require a payment method on file.
    /// Sending <c>automatic</c> in that case makes the provider reject the signup for want of a card.
    /// </summary>
    public string CollectionMethodWithoutPaymentProfile =>
        RelationshipInvoicingEnabled ? "remittance" : "invoice";
}

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Settings of the billing site eShopOnWeb is pointed at. Read once and cached; they describe the
/// site itself rather than any shopper.
/// </summary>
public class BillingSite
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Subdomain { get; init; } = string.Empty;

    /// <summary>The site's primary currency, e.g. "USD". Plan and subscription prices are in it.</summary>
    public string Currency { get; init; } = string.Empty;

    /// <summary>
    /// True when the site runs Relationship Invoicing, which decides what a non-automatic payment
    /// collection method is called: "remittance" here, "invoice" on statement-based sites.
    /// </summary>
    public bool RelationshipInvoicingEnabled { get; init; }

    public string? DefaultPaymentCollectionMethod { get; init; }

    /// <summary>True for a sandbox/test site.</summary>
    public bool TestMode { get; init; }

    /// <summary>
    /// The collection method that bills the shopper by invoice instead of charging a stored card.
    /// </summary>
    public string InvoicePaymentCollectionMethod => RelationshipInvoicingEnabled ? "remittance" : "invoice";
}

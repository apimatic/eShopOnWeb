namespace Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

/// <summary>The subscription attributes Maxio accepts on create.</summary>
public class MaxioSubscriptionAttributes
{
    /// <summary>The plan to subscribe to, by stable handle rather than by id.</summary>
    public string? ProductHandle { get; set; }

    /// <summary>Optional specific price point on the product.</summary>
    public string? ProductPricePointHandle { get; set; }

    /// <summary>An existing Maxio customer to attach the subscription to.</summary>
    public long? CustomerId { get; set; }

    /// <summary>Alternative to <see cref="CustomerId"/>: our own reference for an existing customer.</summary>
    public string? CustomerReference { get; set; }

    /// <summary>
    /// How Maxio should collect payment: "automatic" charges a stored payment method, while
    /// "remittance" (or "invoice" on legacy sites) issues an invoice instead. Omitted to fall back to
    /// the site default.
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }
}

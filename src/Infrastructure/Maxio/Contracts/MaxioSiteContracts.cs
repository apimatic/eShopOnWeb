namespace Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

/// <summary>Maxio <c>Site</c> (<c>components/schemas/Site.yaml</c>).</summary>
public record MaxioSite
{
    public int Id { get; init; }
    public string? Name { get; init; }
    public string? Subdomain { get; init; }

    /// <summary>The site's primary currency, e.g. <c>USD</c>.</summary>
    public string? Currency { get; init; }

    /// <summary>True when the site is a test (sandbox) site.</summary>
    public bool? Test { get; init; }

    /// <summary>
    /// Whether the site runs the Relationship Invoicing architecture, which determines the valid
    /// values of <c>payment_collection_method</c>.
    /// </summary>
    public bool? RelationshipInvoicingEnabled { get; init; }

    /// <summary>The site's default collection method for new subscriptions.</summary>
    public string? DefaultPaymentCollectionMethod { get; init; }
}

/// <summary>Maxio <c>Site Response</c> (<c>components/schemas/Site-Response.yaml</c>).</summary>
public record MaxioSiteResponse
{
    public MaxioSite? Site { get; init; }
}

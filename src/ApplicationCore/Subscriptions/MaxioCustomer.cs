namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>A Maxio customer record, keyed to an eShopOnWeb user via <see cref="Reference"/>.</summary>
public record MaxioCustomer
{
    public int Id { get; init; }

    /// <summary>The eShopOnWeb identifier stored on the Maxio customer (its unique reference).</summary>
    public string? Reference { get; init; }

    public string? Email { get; init; }

    public string? FirstName { get; init; }

    public string? LastName { get; init; }
}

/// <summary>Attributes used to create a new Maxio customer.</summary>
public record NewCustomer
{
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string Email { get; init; }

    /// <summary>The eShopOnWeb identifier persisted as the Maxio customer reference.</summary>
    public required string Reference { get; init; }
}

/// <summary>Attributes used to create a new Maxio subscription for an existing customer.</summary>
public record NewSubscription
{
    /// <summary>The stable product handle to subscribe to.</summary>
    public required string ProductHandle { get; init; }

    /// <summary>The eShopOnWeb user reference identifying the existing Maxio customer.</summary>
    public required string CustomerReference { get; init; }

    /// <summary>
    /// How Maxio collects payment for the subscription. For plans that don't require a stored
    /// payment method this is a non-automatic method (<c>remittance</c> on Relationship Invoicing
    /// sites, <c>invoice</c> on statement-based sites) so signup doesn't attempt an immediate charge.
    /// </summary>
    public string? PaymentCollectionMethod { get; init; }

    /// <summary>
    /// A long, random token that lets Maxio reject an accidentally duplicated create request
    /// (e.g. a network retry) within a 60-minute window with a 409 Conflict.
    /// </summary>
    public string? UniquenessToken { get; init; }
}

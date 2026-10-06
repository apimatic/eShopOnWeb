namespace Maxio.Models;

/// <summary>
/// The subscription payload used when creating a subscription. Mirrors the Create Subscription schema in the Maxio OpenAPI specification.
/// </summary>
public class CreateSubscription
{
    /// <summary>
    /// The API handle of the product for which you are creating a subscription. Required, unless a <see cref="ProductId"/> is given instead.
    /// </summary>
    public string? ProductHandle { get; set; }

    /// <summary>
    /// The product ID of the product for which you are creating a subscription.
    /// </summary>
    public int? ProductId { get; set; }

    /// <summary>
    /// The ID of an existing customer within Maxio. Required, unless a <see cref="CustomerReference"/> or a set of customer attributes is given.
    /// </summary>
    public int? CustomerId { get; set; }

    /// <summary>
    /// The reference value (provided by your app) of an existing customer within Maxio.
    /// </summary>
    public string? CustomerReference { get; set; }

    /// <summary>
    /// The type of payment collection to be used in the subscription. For Relationship Invoicing sites valid options are remittance, automatic, prepaid.
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>
    /// The reference value (provided by your app) for the subscription itself.
    /// </summary>
    public string? Reference { get; set; }
}

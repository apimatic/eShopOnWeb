namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// Payload for enrolling an existing Maxio customer (by reference) into a product/plan.
/// </summary>
public class MaxioSubscriptionCreate
{
    public required string ProductHandle { get; init; }
    public required string CustomerReference { get; init; }

    /// <summary>
    /// One of "automatic", "remittance", "prepaid" or "invoice" (see Collection-Method.yaml).
    /// eShopOnWeb's seeded plans don't require a payment method, so the service defaults this
    /// to "invoice" - it bills the customer without attempting to charge a card on file.
    /// </summary>
    public string PaymentCollectionMethod { get; init; } = "invoice";
}

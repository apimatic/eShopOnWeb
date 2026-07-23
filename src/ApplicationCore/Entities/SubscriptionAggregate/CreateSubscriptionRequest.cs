using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Enrolls an existing provider-side customer in a plan (UC1 step 4).
/// </summary>
public sealed class CreateSubscriptionRequest
{
    public CreateSubscriptionRequest(long customerId, string productHandle, string? paymentCollectionMethod = null)
    {
        CustomerId = Guard.Against.NegativeOrZero(customerId, nameof(customerId));
        ProductHandle = Guard.Against.NullOrWhiteSpace(productHandle, nameof(productHandle));
        PaymentCollectionMethod = paymentCollectionMethod;
    }

    public long CustomerId { get; }

    public string ProductHandle { get; }

    /// <summary>
    /// How the provider should collect payment. Null defers to the provider's site default.
    /// Configuration-driven because a site without a payment gateway can only bill by invoice.
    /// </summary>
    public string? PaymentCollectionMethod { get; }
}

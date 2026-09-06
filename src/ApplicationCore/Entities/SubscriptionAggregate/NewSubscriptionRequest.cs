using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Everything the billing gateway needs to enroll an existing customer in a plan.
/// </summary>
public class NewSubscriptionRequest
{
    public NewSubscriptionRequest(int customerId,
        string planHandle,
        string? pricePointHandle,
        string? reference,
        string uniquenessToken,
        string? paymentCollectionMethod)
    {
        Guard.Against.NegativeOrZero(customerId, nameof(customerId));
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));
        Guard.Against.NullOrWhiteSpace(uniquenessToken, nameof(uniquenessToken));

        CustomerId = customerId;
        PlanHandle = planHandle;
        PricePointHandle = pricePointHandle;
        Reference = reference;
        UniquenessToken = uniquenessToken;
        PaymentCollectionMethod = paymentCollectionMethod;
    }

    public int CustomerId { get; }
    public string PlanHandle { get; }
    public string? PricePointHandle { get; }

    /// <summary>Our own identifier for the subscription. Must be unique across the billing site.</summary>
    public string? Reference { get; }

    /// <summary>
    /// Guards against a request that was received but whose response was lost: a repeat within the
    /// billing system's dedupe window is rejected instead of creating a second subscription.
    /// </summary>
    public string UniquenessToken { get; }

    public string? PaymentCollectionMethod { get; }
}

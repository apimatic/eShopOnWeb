namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>Attributes used to create a subscription for an existing billing customer.</summary>
public sealed record NewSubscription
{
    public required long CustomerId { get; init; }

    public required string PlanHandle { get; init; }

    public required string PaymentCollectionMethod { get; init; }

    /// <summary>
    /// Deterministic token the provider uses to reject a replay of this exact request within its
    /// duplicate-prevention window.
    /// </summary>
    public required string IdempotencyToken { get; init; }
}

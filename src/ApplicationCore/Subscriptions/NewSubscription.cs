namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>Instruction to enroll an existing Maxio customer in a plan.</summary>
public sealed class NewSubscription
{
    public required long CustomerId { get; init; }

    /// <summary>Maxio product API handle to subscribe to.</summary>
    public required string PlanHandle { get; init; }

    /// <summary>
    /// Reference eShopOnWeb assigns to the subscription. Maxio enforces uniqueness on this value,
    /// which is what makes a repeated signup a no-op rather than a duplicate enrollment.
    /// </summary>
    public required string Reference { get; init; }
}

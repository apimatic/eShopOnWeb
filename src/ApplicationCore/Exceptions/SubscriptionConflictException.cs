namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A subscription already occupies the idempotency scope the caller asked for, but it has reached
/// the end of its life so returning it would be misleading. Enrolling again is a deliberate act:
/// the caller must supply a distinct idempotency key.
/// </summary>
public class SubscriptionConflictException : BillingException
{
    public SubscriptionConflictException(long existingSubscriptionId, string existingState, string planHandle)
        : base($"A previous subscription (id {existingSubscriptionId}) to plan '{planHandle}' exists for this " +
               $"subscriber and is in state '{existingState}'. Supply a distinct idempotencyKey to subscribe again.")
    {
        ExistingSubscriptionId = existingSubscriptionId;
        ExistingState = existingState;
        PlanHandle = planHandle;
    }

    public long ExistingSubscriptionId { get; }

    public string ExistingState { get; }

    public string PlanHandle { get; }
}

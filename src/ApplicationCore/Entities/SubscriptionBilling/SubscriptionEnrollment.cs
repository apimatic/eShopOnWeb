using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBilling;

/// <summary>
/// Records the link between an eShopOnWeb subscriber and the Maxio (Advanced Billing)
/// customer + subscription that backs their enrollment. Maxio remains the system of
/// record; this row is a local projection used to make enrollment idempotent and easy
/// to reconcile. It is intentionally scoped to (SubscriberKey, ProductHandle) so that a
/// single user holds at most one current enrollment per plan.
/// </summary>
public class SubscriptionEnrollment : BaseEntity
{
    private SubscriptionEnrollment()
    {
        // required by EF Core
    }

    public SubscriptionEnrollment(
        string subscriberKey,
        string productHandle,
        string customerReference,
        int maxioCustomerId,
        int maxioSubscriptionId,
        string state)
    {
        SubscriberKey = subscriberKey ?? throw new ArgumentNullException(nameof(subscriberKey));
        ProductHandle = productHandle ?? throw new ArgumentNullException(nameof(productHandle));
        CustomerReference = customerReference ?? throw new ArgumentNullException(nameof(customerReference));
        MaxioCustomerId = maxioCustomerId;
        MaxioSubscriptionId = maxioSubscriptionId;
        State = state ?? throw new ArgumentNullException(nameof(state));
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public string SubscriberKey { get; private set; }

    public string CustomerReference { get; private set; }

    public string ProductHandle { get; private set; }

    public int MaxioCustomerId { get; private set; }

    public int MaxioSubscriptionId { get; private set; }

    public string State { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public void UpdateEnrollment(int maxioCustomerId, int maxioSubscriptionId, string state)
    {
        MaxioCustomerId = maxioCustomerId;
        MaxioSubscriptionId = maxioSubscriptionId;
        State = state ?? throw new ArgumentNullException(nameof(state));
        UpdatedAtUtc = DateTime.UtcNow;
    }
}

using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

/// <summary>
/// Local claim + ledger row for a subscription of one eShop user to one plan. The composite unique index
/// on (<see cref="BuyerId"/>, <see cref="PlanHandle"/>) is the duplicate-prevention claim: it is inserted
/// (state <c>pending</c>) BEFORE the Maxio create call and completed with the returned id/state after. A
/// second concurrent subscribe to the same plan is rejected by the constraint. Maxio is the system of
/// record; this row is a claim/cache.
/// </summary>
public class MaxioSubscriptionRecord : BaseEntity
{
    public const string StatePending = "pending";

    public string BuyerId { get; private set; } = default!;
    public string PlanHandle { get; private set; } = default!;

    /// <summary>Deterministic Maxio subscription <c>reference</c> (<c>eshop-{buyerId}-{planHandle}</c>).</summary>
    public string Reference { get; private set; } = default!;

    public int? MaxioCustomerId { get; private set; }
    public int? MaxioSubscriptionId { get; private set; }

    /// <summary>Local claim status; <c>pending</c> until the provider confirms, then the Maxio state.</summary>
    public string State { get; private set; } = StatePending;

    public DateTimeOffset CreatedDate { get; private set; }
    public DateTimeOffset? UpdatedDate { get; private set; }

    private MaxioSubscriptionRecord() { } // EF

    public MaxioSubscriptionRecord(string buyerId, string planHandle, string reference, DateTimeOffset createdDate)
    {
        BuyerId = buyerId;
        PlanHandle = planHandle;
        Reference = reference;
        State = StatePending;
        CreatedDate = createdDate;
    }

    /// <summary>Completed after the provider confirms the subscription (row 12: local row first, provider, then settle).</summary>
    public void MarkProvisioned(int maxioSubscriptionId, int? maxioCustomerId, string? state, DateTimeOffset updatedDate)
    {
        MaxioSubscriptionId = maxioSubscriptionId;
        MaxioCustomerId = maxioCustomerId;
        if (!string.IsNullOrWhiteSpace(state))
        {
            State = state!;
        }
        UpdatedDate = updatedDate;
    }
}

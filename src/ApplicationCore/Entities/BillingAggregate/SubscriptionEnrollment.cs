using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

/// <summary>
/// One user's enrollment in one plan. The key is "{customer reference}|{plan handle}", so a user can hold
/// at most one claim per plan; <see cref="SubscriptionReference"/> is the reference sent to the provider
/// with the create call and is what an ambiguous outcome is looked up by.
/// </summary>
public class SubscriptionEnrollment : BillingClaim, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SubscriptionEnrollment() { }

    public SubscriptionEnrollment(string customerReference, string planHandle, DateTimeOffset now)
        : base(KeyFor(customerReference, planHandle), now)
    {
        CustomerReference = customerReference;
        PlanHandle = Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));
        SubscriptionReference = $"eshop-sub-{Guid.NewGuid():N}";
    }

    public string CustomerReference { get; private set; }
    public string PlanHandle { get; private set; }
    public string SubscriptionReference { get; private set; }
    public int? ProviderSubscriptionId { get; private set; }

    public static string KeyFor(string customerReference, string planHandle) =>
        $"{Guard.Against.NullOrWhiteSpace(customerReference, nameof(customerReference))}|{planHandle.Trim().ToLowerInvariant()}";

    public void Activate(int providerSubscriptionId, DateTimeOffset now)
    {
        ProviderSubscriptionId = providerSubscriptionId;
        State = BillingClaimState.Active;
        Touch(now);
    }
}

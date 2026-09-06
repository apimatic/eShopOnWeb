using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class Subscription : BaseEntity, IAggregateRoot
{
    public int BuyerId { get; private set; }
    public string IdentityId { get; private set; } = null!;
    public int MaxioSubscriptionId { get; private set; }
    public int MaxioCustomerId { get; private set; }
    public string PlanHandle { get; private set; } = null!;
    public string PlanName { get; private set; } = null!;
    public decimal PriceInCents { get; private set; }
    public string Status { get; private set; } = null!;
    public DateTime CurrentPeriodStartAt { get; private set; }
    public DateTime CurrentPeriodEndAt { get; private set; }
    public DateTime? CanceledAt { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    #pragma warning disable CS8618
    private Subscription() { }

    public Subscription(
        int buyerId,
        string identityId,
        int maxioSubscriptionId,
        int maxioCustomerId,
        string planHandle,
        string planName,
        decimal priceInCents,
        string status,
        DateTime currentPeriodStartAt,
        DateTime currentPeriodEndAt)
    {
        Guard.Against.NegativeOrZero(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(identityId, nameof(identityId));
        Guard.Against.NegativeOrZero(maxioSubscriptionId, nameof(maxioSubscriptionId));
        Guard.Against.NegativeOrZero(maxioCustomerId, nameof(maxioCustomerId));
        Guard.Against.NullOrEmpty(planHandle, nameof(planHandle));
        Guard.Against.NullOrEmpty(planName, nameof(planName));
        Guard.Against.NegativeOrZero((int)priceInCents, nameof(priceInCents));
        Guard.Against.NullOrEmpty(status, nameof(status));

        BuyerId = buyerId;
        IdentityId = identityId;
        MaxioSubscriptionId = maxioSubscriptionId;
        MaxioCustomerId = maxioCustomerId;
        PlanHandle = planHandle;
        PlanName = planName;
        PriceInCents = priceInCents;
        Status = status;
        CurrentPeriodStartAt = currentPeriodStartAt;
        CurrentPeriodEndAt = currentPeriodEndAt;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }
}

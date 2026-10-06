using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

/// <summary>
/// A record that maps an eShopOnWeb user to a recurring subscription managed in
/// Maxio Advanced Billing. Maxio remains the billing system of record; this entity
/// is a local cross-reference that also guards against duplicate subscribes.
/// </summary>
public class SubscriptionRecord : BaseEntity, IAggregateRoot
{
    public SubscriptionRecord(string ownerId, string planHandle, int maxioCustomerId, int maxioSubscriptionId)
    {
        OwnerId = ownerId;
        PlanHandle = planHandle;
        MaxioCustomerId = maxioCustomerId;
        MaxioSubscriptionId = maxioSubscriptionId;
        CreatedOn = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// The eShopOnWeb application user id (ApplicationUser.Id) who owns the subscription.
    /// This value is used as the Maxio customer <c>reference</c>.
    /// </summary>
    public string OwnerId { get; private set; }

    /// <summary>
    /// The Maxio product (plan) handle this subscription was created against.
    /// </summary>
    public string PlanHandle { get; private set; }

    /// <summary>
    /// The Maxio customer id that was linked to the owner.
    /// </summary>
    public int MaxioCustomerId { get; private set; }

    /// <summary>
    /// The Maxio subscription id created in Advanced Billing.
    /// </summary>
    public int MaxioSubscriptionId { get; private set; }

    /// <summary>
    /// When the subscription was created in this application.
    /// </summary>
    public DateTimeOffset CreatedOn { get; private set; }
}
using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

/// <summary>
/// Local link between an eShopOnWeb shopper and a Maxio Advanced Billing subscription.
/// The primary key is the Maxio subscription reference this application generates
/// ("{userId}:{productHandle}", suffixed -2, -3, ... when a previous subscription on the same
/// reference reached an end-of-life state). Inserting a row is the duplicate-prevention claim
/// for concurrent subscription creation.
/// </summary>
public class MaxioSubscriptionLink : IAggregateRoot
{
    /// <summary>
    /// The Maxio subscription reference value. Primary key of the claim.
    /// </summary>
    public string MaxioReference { get; set; } = string.Empty;

    /// <summary>
    /// The eShopOnWeb user id the subscription belongs to.
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// The Maxio product (plan) handle the subscription targets.
    /// </summary>
    public string ProductHandle { get; set; } = string.Empty;

    /// <summary>
    /// The numeric Maxio subscription id, recorded once the subscription exists. 0 while the claim is pending.
    /// </summary>
    public int MaxioSubscriptionId { get; set; }

    /// <summary>
    /// The numeric Maxio customer id owning the subscription. 0 while the claim is pending.
    /// </summary>
    public int MaxioCustomerId { get; set; }

    /// <summary>
    /// The last observed Maxio subscription state; "pending" while the claim is unresolved.
    /// </summary>
    public string State { get; set; } = "pending";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
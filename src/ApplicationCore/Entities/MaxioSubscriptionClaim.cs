using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

/// <summary>
/// Claim row written before a Maxio subscription is created. The RequestId is the
/// deterministic Maxio subscription reference for (user, plan), used as the primary
/// key so a concurrent second claim for the same (user, plan) is rejected by the store.
/// Status is Pending until the provider confirms the subscription, or Unknown when a
/// connection failure left the outcome unsettled.
/// </summary>
public sealed class MaxioSubscriptionClaim : IAggregateRoot
{
    public const string StatusPending = "Pending";
    public const string StatusConfirmed = "Confirmed";
    public const string StatusUnknown = "Unknown";

    /// <summary>
    /// The deterministic Maxio subscription reference, "eshopweb:{userId}:{productHandle}".
    /// Primary key.
    /// </summary>
    public string RequestId { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    public string ProductHandle { get; set; } = string.Empty;

    public string Status { get; set; } = MaxioSubscriptionClaim.StatusPending;

    public int? MaxioSubscriptionId { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset? UpdatedUtc { get; set; }
}
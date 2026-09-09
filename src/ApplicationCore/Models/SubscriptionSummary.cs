using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>
/// The current, billing-system-of-record view of one of a user's subscriptions.
/// </summary>
public record SubscriptionSummary(
    int SubscriptionId,
    string ProductHandle,
    string ProductName,
    decimal Price,
    string State,
    DateTime? NextBillingDate,
    DateTime? ActivatedAt,
    int MaxioCustomerId,
    string? MaxioReference);

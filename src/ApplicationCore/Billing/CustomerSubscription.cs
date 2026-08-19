using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// A Maxio subscription belonging to an eShopOnWeb shopper.
/// </summary>
public sealed record CustomerSubscription(
    int Id,
    string State,
    string? ProductHandle,
    string? ProductName,
    decimal Price,
    DateTimeOffset? NextBillingDate,
    DateTimeOffset CreatedAt,
    string? Reference);

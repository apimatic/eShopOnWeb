using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// A Maxio subscription belonging to the authenticated shopper.
/// </summary>
public sealed record CustomerSubscription(
    int Id,
    string State,
    string? ProductHandle,
    string? ProductName,
    long PriceInCents,
    DateTimeOffset? NextBillingAt);

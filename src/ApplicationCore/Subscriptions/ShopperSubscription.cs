using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A Maxio Advanced Billing subscription owned by the authenticated shopper.
/// </summary>
public sealed record ShopperSubscription(
    int Id,
    string State,
    string ProductHandle,
    string ProductName,
    long PriceInCents,
    DateTimeOffset? NextBillingAt)
{
    public decimal Price => PriceInCents / 100m;
}

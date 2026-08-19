using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

public sealed record BillingShopper(string Id, string Email, string? UserName);

public sealed record SubscriptionPlan(
    int Id,
    string Handle,
    string Name,
    string? Description,
    decimal Price,
    long PriceInCents,
    int Interval,
    string IntervalUnit);

public sealed record ShopperSubscription(
    int Id,
    string ProductHandle,
    string ProductName,
    decimal Price,
    long PriceInCents,
    string State,
    DateTimeOffset? NextBillingDate);

public sealed record SubscribeResult(ShopperSubscription Subscription, bool Created);

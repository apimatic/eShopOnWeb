using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

/// <summary>
/// The authenticated eShopOnWeb shopper, mapped onto a Maxio customer via <see cref="UserId"/> as the customer reference.
/// </summary>
public sealed record ShopperIdentity(string UserId, string Email, string FirstName, string LastName);

/// <summary>
/// A sellable Maxio product (plan) in the configured product family.
/// </summary>
public sealed record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    decimal Price,
    long PriceInCents,
    int Interval,
    string IntervalUnit);

/// <summary>
/// A Maxio subscription belonging to the current shopper. Maxio is the system of record.
/// </summary>
public sealed record ShopperSubscription(
    int Id,
    string State,
    string ProductHandle,
    string ProductName,
    decimal Price,
    long PriceInCents,
    DateTimeOffset? NextBillingAt);

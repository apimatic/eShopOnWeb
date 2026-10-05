using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models.MaxioBilling;

/// <summary>
/// A subscription as recorded in the billing system, confirmed back to the shopper:
/// plan, price, state and next billing date.
/// </summary>
public record SubscriptionInfo(
    int MaxioSubscriptionId,
    string ProductHandle,
    string? ProductName,
    string State,
    long? PriceInCents,
    DateTimeOffset? CurrentPeriodEndsAt,
    string Reference);

/// <summary>
/// The eShop identity a subscription is billed to, resolved from the authenticated caller's token.
/// </summary>
public record ShopperIdentity(string UserId, string Username, string Email, string FirstName, string LastName);
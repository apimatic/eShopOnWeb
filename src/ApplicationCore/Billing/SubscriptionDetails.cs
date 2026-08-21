using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed record SubscriptionDetails(
    int Id,
    string ProductHandle,
    string? ProductName,
    string? PricePointHandle,
    long? PriceInCents,
    string? Currency,
    string? State,
    DateTimeOffset? NextBillingDate,
    int? CustomerId,
    string? CustomerReference,
    string? Reference);

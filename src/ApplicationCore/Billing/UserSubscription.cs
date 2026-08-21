using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed record UserSubscription(
    string? ProductHandle,
    string? ProductName,
    decimal? Price,
    string? State,
    DateTimeOffset? NextBillingDate,
    string? Reference);

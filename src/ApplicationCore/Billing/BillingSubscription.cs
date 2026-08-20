using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed record BillingSubscription(
    int Id,
    string ProductHandle,
    string ProductName,
    decimal Price,
    string State,
    DateTimeOffset? NextBillingDate,
    string? Reference,
    string? ProductFamilyHandle);

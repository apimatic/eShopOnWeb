using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>
/// A shopper's subscription as recorded in Maxio Advanced Billing (the system of record).
/// </summary>
public record CustomerSubscription(
    long Id,
    string? Reference,
    string State,
    string? ProductHandle,
    string? ProductName,
    long ProductPriceInCents,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? NextAssessmentAt,
    DateTimeOffset CreatedAt,
    long MaxioCustomerId);

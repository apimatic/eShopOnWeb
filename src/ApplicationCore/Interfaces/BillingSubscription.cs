using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A provider-agnostic view of a billing-provider subscription's current state.</summary>
public record BillingSubscription(
    int Id,
    int CustomerId,
    string ProductHandle,
    int ProductId,
    string State,
    bool CancelAtEndOfPeriod,
    DateTimeOffset? CurrentPeriodEndsAt,
    int? ProductVersionNumber);

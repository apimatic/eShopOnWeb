using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Read models returned by <see cref="IMaxioApiClient"/>. These are normalized
/// projections of the Maxio Billing API payloads (the API wraps objects in
/// envelope keys, e.g. {"product": {...}}) and use stable handles wherever
/// possible because numeric Maxio IDs are reassigned when a site is re-seeded.
/// </summary>
public class MaxioProduct
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Handle { get; init; } = string.Empty;
    public string? Description { get; init; }
    public long PriceInCents { get; init; }
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = "month";
    public int? TrialInterval { get; init; }
    public string? TrialIntervalUnit { get; init; }
    public bool RequireCreditCard { get; init; }
    public bool Taxable { get; init; }
    public DateTime? ArchivedAt { get; init; }
    public int ProductFamilyId { get; init; }
    public string ProductFamilyHandle { get; init; } = string.Empty;
}

public class MaxioCustomer
{
    public int Id { get; init; }
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? Reference { get; init; }
}

public class MaxioSubscription
{
    public int Id { get; init; }
    public string State { get; init; } = string.Empty;
    public int CustomerId { get; init; }
    public string? Reference { get; init; }
    public string? ProductHandle { get; init; }
    public string? ProductName { get; init; }
    public long ProductPriceInCents { get; init; }
    public long BalanceInCents { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? ActivatedAt { get; init; }
    public DateTime? CanceledAt { get; init; }
    /// <summary>
    /// End of the current billing period (i.e. the next regularly scheduled charge).
    /// </summary>
    public DateTime? CurrentPeriodEndsAt { get; init; }
}

/// <summary>
/// Details for creating a Maxio customer.
/// </summary>
public class MaxioNewCustomer
{
    public string Reference { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
}

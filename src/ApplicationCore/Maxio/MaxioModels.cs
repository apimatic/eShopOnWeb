using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>A subscribable plan (an Advanced Billing "Product") within a Product Family.</summary>
public class SubscriptionPlan
{
    public long Id { get; init; }
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int PriceInCents { get; init; }
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
    public string ProductFamilyHandle { get; init; } = string.Empty;
    public bool RequireCreditCard { get; init; }
    public bool Archived { get; init; }
}

/// <summary>An Advanced Billing customer record (subset of fields we surface).</summary>
public class MaxioCustomer
{
    public long Id { get; init; }
    public string? Reference { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Email { get; init; }
}

/// <summary>Input used to create an Advanced Billing customer idempotently.</summary>
public class MaxioCustomerInput
{
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public required string Email { get; init; }
    public required string Reference { get; init; }
}

/// <summary>Input used to create a subscription against an existing customer.</summary>
public class MaxioSubscriptionInput
{
    public required long CustomerId { get; init; }
    public required string ProductHandle { get; init; }
    public string? Reference { get; init; }
    /// <summary>Optional Advanced Billing collection method (see spec Collection-Method enum).</summary>
    public string? PaymentCollectionMethod { get; init; }
}

/// <summary>An Advanced Billing subscription (subset of fields we surface).</summary>
public class Subscription
{
    public long Id { get; init; }
    public string State { get; init; } = string.Empty;
    public string? Reference { get; init; }
    public long CustomerId { get; init; }
    public string ProductHandle { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public int PriceInCents { get; init; }
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    public DateTimeOffset? NextAssessmentAt { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? ActivatedAt { get; init; }
    public DateTimeOffset? CanceledAt { get; init; }
}

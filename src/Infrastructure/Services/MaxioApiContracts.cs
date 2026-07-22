using System;
using System.Text.Json;

namespace Microsoft.eShopWeb.Infrastructure.Services;

// Wire contracts for the subset of the Maxio Advanced Billing OpenAPI specification this
// integration uses. Property names map to the specification's snake_case fields through the
// serializer's naming policy; nothing outside this file knows these shapes exist.

internal sealed class MaxioProductResponse
{
    public MaxioProduct? Product { get; set; }
}

internal sealed class MaxioProduct
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public bool RequireCreditCard { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public MaxioProductFamily? ProductFamily { get; set; }
}

internal sealed class MaxioProductFamily
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
}

internal sealed class MaxioCustomerResponse
{
    public MaxioCustomer? Customer { get; set; }
}

internal sealed class MaxioCustomer
{
    public int Id { get; set; }
    public string? Reference { get; set; }
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}

internal sealed class MaxioCreateCustomerRequest
{
    public MaxioCustomerAttributes Customer { get; set; } = new();
}

internal sealed class MaxioCustomerAttributes
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

internal sealed class MaxioSubscriptionResponse
{
    public MaxioSubscription? Subscription { get; set; }
}

internal sealed class MaxioSubscription
{
    public int Id { get; set; }
    public string? State { get; set; }
    public long BalanceInCents { get; set; }
    public long ProductPriceInCents { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public DateTimeOffset? DelayedCancelAt { get; set; }
    public bool? CancelAtEndOfPeriod { get; set; }
    public MaxioCustomer? Customer { get; set; }
    public MaxioProduct? Product { get; set; }
}

internal sealed class MaxioCreateSubscriptionRequest
{
    public MaxioCreateSubscription Subscription { get; set; } = new();
}

internal sealed class MaxioCreateSubscription
{
    public string? ProductHandle { get; set; }
    public int? CustomerId { get; set; }
}

internal sealed class MaxioComponentResponse
{
    public MaxioComponent? Component { get; set; }
}

internal sealed class MaxioComponent
{
    public int Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public string? Kind { get; set; }
    public string? PricingScheme { get; set; }

    /// <summary>Per-unit price as a decimal string, e.g. "0.01". Only populated for per_unit schemes.</summary>
    public string? UnitPrice { get; set; }

    public string? UnitName { get; set; }
    public int ProductFamilyId { get; set; }
    public string? ProductFamilyHandle { get; set; }
    public bool Archived { get; set; }
}

internal sealed class MaxioSubscriptionComponentResponse
{
    public MaxioSubscriptionComponent? Component { get; set; }
}

internal sealed class MaxioSubscriptionComponent
{
    public int ComponentId { get; set; }
    public string? ComponentHandle { get; set; }
    public int SubscriptionId { get; set; }
    public string? Name { get; set; }
    public string? Kind { get; set; }

    /// <summary>The accumulated, not-yet-invoiced units for the current period.</summary>
    public decimal? UnitBalance { get; set; }
}

internal sealed class MaxioCreateUsageRequest
{
    public MaxioCreateUsage Usage { get; set; } = new();
}

internal sealed class MaxioCreateUsage
{
    public decimal Quantity { get; set; }
    public string? Memo { get; set; }
}

internal sealed class MaxioUsageResponse
{
    public MaxioUsage? Usage { get; set; }
}

internal sealed class MaxioUsage
{
    public long Id { get; set; }
    public string? Memo { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public int ComponentId { get; set; }
    public string? ComponentHandle { get; set; }
    public int SubscriptionId { get; set; }

    /// <summary>The specification allows either a number or a decimal string here.</summary>
    public JsonElement Quantity { get; set; }
}

internal sealed class MaxioMigrationRequest
{
    public MaxioMigrationOptions Migration { get; set; } = new();
}

internal sealed class MaxioMigrationOptions
{
    public string? ProductHandle { get; set; }

    /// <summary>True keeps the billing period and issues a prorated charge; false resets the period.</summary>
    public bool PreservePeriod { get; set; }
}

internal sealed class MaxioMigrationPreviewResponse
{
    public MaxioMigrationPreview? Migration { get; set; }
}

internal sealed class MaxioMigrationPreview
{
    public long ProratedAdjustmentInCents { get; set; }
    public long ChargeInCents { get; set; }
    public long PaymentDueInCents { get; set; }
    public long CreditAppliedInCents { get; set; }
}

internal sealed class MaxioCancellationRequest
{
    public MaxioCancellationOptions Subscription { get; set; } = new();
}

internal sealed class MaxioCancellationOptions
{
    public string? CancellationMessage { get; set; }
}

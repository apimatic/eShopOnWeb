using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Services;

// Wire-shape DTOs for the Maxio Advanced Billing HTTP API (maxio-spec/openapi.yaml).
// (De)serialized with JsonNamingPolicy.SnakeCaseLower, so these PascalCase properties map
// 1:1 onto Maxio's snake_case JSON field names (e.g. ProductHandle <-> product_handle).

internal class MaxioCustomerWire
{
    public int Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

internal class MaxioCustomerEnvelope
{
    public MaxioCustomerWire? Customer { get; set; }
}

internal class MaxioCreateCustomer
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
}

internal class MaxioCreateCustomerEnvelope
{
    public MaxioCreateCustomer Customer { get; set; } = new();
}

internal class MaxioProductWire
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Handle { get; set; }
    public int PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
}

internal class MaxioProductEnvelope
{
    public MaxioProductWire? Product { get; set; }
}

internal class MaxioSubscriptionWire
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public bool? CancelAtEndOfPeriod { get; set; }
    public MaxioProductWire? Product { get; set; }
    public MaxioCustomerWire? Customer { get; set; }
}

internal class MaxioSubscriptionEnvelope
{
    public MaxioSubscriptionWire? Subscription { get; set; }
}

internal class MaxioCreateSubscription
{
    public string ProductHandle { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public string PaymentCollectionMethod { get; set; } = "remittance";
}

internal class MaxioCreateSubscriptionEnvelope
{
    public MaxioCreateSubscription Subscription { get; set; } = new();
}

internal class MaxioDelayedProductChange
{
    public string ProductHandle { get; set; } = string.Empty;
    public bool ProductChangeDelayed { get; set; } = true;
}

internal class MaxioDelayedProductChangeEnvelope
{
    public MaxioDelayedProductChange Subscription { get; set; } = new();
}

internal class MaxioMigration
{
    public string ProductHandle { get; set; } = string.Empty;
    public bool PreservePeriod { get; set; } = true;
}

internal class MaxioMigrationEnvelope
{
    public MaxioMigration Migration { get; set; } = new();
}

internal class MaxioMigrationPreviewWire
{
    public int ProratedAdjustmentInCents { get; set; }
    public int ChargeInCents { get; set; }
    public int PaymentDueInCents { get; set; }
    public int CreditAppliedInCents { get; set; }
}

internal class MaxioMigrationPreviewEnvelope
{
    public MaxioMigrationPreviewWire? Migration { get; set; }
}

internal class MaxioComponentWire
{
    public int Id { get; set; }
    public string? Handle { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
}

internal class MaxioComponentEnvelope
{
    public MaxioComponentWire? Component { get; set; }
}

internal class MaxioSubscriptionComponentWire
{
    public int UnitBalance { get; set; }
}

internal class MaxioSubscriptionComponentEnvelope
{
    public MaxioSubscriptionComponentWire? Component { get; set; }
}

internal class MaxioCreateUsage
{
    public int Quantity { get; set; }
    public string? Memo { get; set; }
}

internal class MaxioCreateUsageEnvelope
{
    public MaxioCreateUsage Usage { get; set; } = new();
}

internal class MaxioUsageWire
{
    public long Id { get; set; }
    public string? Memo { get; set; }
    public int Quantity { get; set; }
}

internal class MaxioUsageEnvelope
{
    public MaxioUsageWire? Usage { get; set; }
}

internal class MaxioCancellationOptions
{
    public string? CancellationMessage { get; set; }
    public string? ReasonCode { get; set; }
}

internal class MaxioCancellationEnvelope
{
    public MaxioCancellationOptions Subscription { get; set; } = new();
}

internal class MaxioAutoResume
{
    public DateTimeOffset? AutomaticallyResumeAt { get; set; }
}

internal class MaxioPauseEnvelope
{
    public MaxioAutoResume Hold { get; set; } = new();
}

internal class MaxioErrorArrayResponse
{
    public List<string>? Errors { get; set; }
}

internal class MaxioSingleErrorResponse
{
    public string? Error { get; set; }
}

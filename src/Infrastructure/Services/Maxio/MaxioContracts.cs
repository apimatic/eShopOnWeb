using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Services.Maxio;

// Wire contracts for the Maxio Advanced Billing REST API, mirroring the operations used by
// MaxioBillingClient in maxio-spec/openapi.yaml. Property names map to the provider's
// snake_case fields through MaxioJson's naming policy. These types never leave Infrastructure.

internal class ProductEnvelope
{
    public ProductResource? Product { get; set; }
}

internal class ProductResource
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
    public ProductFamilyResource? ProductFamily { get; set; }
}

internal class ProductFamilyEnvelope
{
    public ProductFamilyResource? ProductFamily { get; set; }
}

internal class ProductFamilyResource
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
}

internal class ComponentEnvelope
{
    public ComponentResource? Component { get; set; }
}

internal class ComponentResource
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Kind { get; set; }
    public string? PricingScheme { get; set; }

    /// <summary>Reported by Maxio in major currency units, e.g. "0.01".</summary>
    public decimal? UnitPrice { get; set; }
    public string? UnitName { get; set; }
    public int ProductFamilyId { get; set; }
    public string? ProductFamilyHandle { get; set; }
    public string? ProductFamilyName { get; set; }
    public bool Archived { get; set; }
}

internal class CustomerEnvelope
{
    public CustomerResource? Customer { get; set; }
}

internal class CustomerResource
{
    public int Id { get; set; }
    public string? Reference { get; set; }
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}

internal class CreateCustomerRequest
{
    public CreateCustomerAttributes Customer { get; set; } = new();
}

internal class CreateCustomerAttributes
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

internal class SubscriptionEnvelope
{
    public SubscriptionResource? Subscription { get; set; }
}

internal class SubscriptionResource
{
    public int Id { get; set; }
    public string? State { get; set; }
    public long BalanceInCents { get; set; }
    public long ProductPriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public bool? CancelAtEndOfPeriod { get; set; }
    public DateTimeOffset? DelayedCancelAt { get; set; }
    public CustomerResource? Customer { get; set; }
    public ProductResource? Product { get; set; }
}

internal class CreateSubscriptionRequest
{
    public CreateSubscriptionAttributes Subscription { get; set; } = new();
}

internal class CreateSubscriptionAttributes
{
    public string? ProductHandle { get; set; }
    public int? CustomerId { get; set; }
    public string? CustomerReference { get; set; }
}

internal class UpdateSubscriptionRequest
{
    public UpdateSubscriptionAttributes Subscription { get; set; } = new();
}

internal class UpdateSubscriptionAttributes
{
    public string? ProductHandle { get; set; }
    public bool? ProductChangeDelayed { get; set; }
}

internal class CancellationRequest
{
    public CancellationOptions Subscription { get; set; } = new();
}

internal class CancellationOptions
{
    public string? CancellationMessage { get; set; }
}

internal class UsageEnvelope
{
    public UsageResource? Usage { get; set; }
}

internal class UsageResource
{
    public long Id { get; set; }
    public string? Memo { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>Maxio reports this as either a number or a string, e.g. <c>"20.0"</c>.</summary>
    public decimal Quantity { get; set; }
    public int ComponentId { get; set; }
    public string? ComponentHandle { get; set; }
    public int SubscriptionId { get; set; }
}

internal class CreateUsageRequest
{
    public CreateUsageAttributes Usage { get; set; } = new();
}

internal class CreateUsageAttributes
{
    public decimal Quantity { get; set; }
    public string? Memo { get; set; }
}

internal class SubscriptionComponentEnvelope
{
    public SubscriptionComponentResource? Component { get; set; }
}

internal class SubscriptionComponentResource
{
    public int ComponentId { get; set; }
    public string? ComponentHandle { get; set; }
    public int SubscriptionId { get; set; }
    public string? Name { get; set; }
    public string? Kind { get; set; }

    /// <summary>The accumulated metered usage for the current period.</summary>
    public decimal? UnitBalance { get; set; }
    public decimal? AllocatedQuantity { get; set; }
}

internal class MigrationPreviewEnvelope
{
    public MigrationPreviewResource? Migration { get; set; }
}

internal class MigrationPreviewResource
{
    public long ProratedAdjustmentInCents { get; set; }
    public long ChargeInCents { get; set; }
    public long PaymentDueInCents { get; set; }
    public long CreditAppliedInCents { get; set; }
}

internal class MigrationRequest
{
    public MigrationAttributes Migration { get; set; } = new();
}

internal class MigrationAttributes
{
    public string? ProductHandle { get; set; }

    /// <summary>True keeps the billing period and issues a prorated charge for the new plan.</summary>
    public bool PreservePeriod { get; set; }
    public bool IncludeTrial { get; set; }
    public bool IncludeInitialCharge { get; set; }
}

internal class DelayedCancellationResponse
{
    public string? Message { get; set; }
}

internal class PauseRequest
{
    public AutoResumeOptions Hold { get; set; } = new();
}

internal class AutoResumeOptions
{
    public DateTimeOffset? AutomaticallyResumeAt { get; set; }
}

internal class ReactivateSubscriptionRequest
{
    /// <summary>True asks Maxio to resume the existing billing period where it can.</summary>
    public bool? Resume { get; set; }
}

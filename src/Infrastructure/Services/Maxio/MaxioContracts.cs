using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Services.Maxio;

// Wire contracts for the Maxio Advanced Billing API, mirroring the schemas in
// maxio-spec/components/schemas. Every response is wrapped in a single top-level key and list
// endpoints return an array of those wrappers. Property names are snake_case and are mapped by the
// shared naming policy in MaxioJson, so only the exceptions need an explicit attribute.

internal sealed class ProductFamilyEnvelope
{
    public MaxioProductFamily? ProductFamily { get; set; }
}

internal sealed class MaxioProductFamily
{
    public int Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
}

internal sealed class ProductEnvelope
{
    public MaxioProduct? Product { get; set; }
}

/// <summary>Maxio's Product — an eShopOnWeb subscription plan. See Product.yaml.</summary>
internal sealed class MaxioProduct
{
    public int Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }

    /// <summary>The recurring price in integer cents — the provider's canonical money field.</summary>
    public long PriceInCents { get; set; }

    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public bool RequireCreditCard { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public MaxioProductFamily? ProductFamily { get; set; }
}

internal sealed class ComponentEnvelope
{
    public MaxioComponent? Component { get; set; }
}

/// <summary>Maxio's Component. See Component.yaml — note <c>unit_price</c> is a string.</summary>
internal sealed class MaxioComponent
{
    public int Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }

    /// <summary>One of metered_component, quantity_based_component, on_off_component, prepaid_usage_component, event_based_component.</summary>
    public string? Kind { get; set; }

    public string? PricingScheme { get; set; }

    [JsonConverter(typeof(FlexibleNullableDecimalConverter))]
    public decimal? UnitPrice { get; set; }

    public string? UnitName { get; set; }
    public int ProductFamilyId { get; set; }
    public string? ProductFamilyHandle { get; set; }
    public bool Archived { get; set; }
}

internal sealed class CustomerEnvelope
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

internal sealed class CreateCustomerRequest
{
    public CreateCustomerBody Customer { get; set; } = new();
}

internal sealed class CreateCustomerBody
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Reference { get; set; }
}

internal sealed class SubscriptionEnvelope
{
    public MaxioSubscription? Subscription { get; set; }
}

/// <summary>Maxio's Subscription. See Subscription.yaml.</summary>
internal sealed class MaxioSubscription
{
    public int Id { get; set; }
    public string? State { get; set; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public bool? CancelAtEndOfPeriod { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public long ProductPriceInCents { get; set; }
    public string? NextProductHandle { get; set; }
    public MaxioCustomer? Customer { get; set; }
    public MaxioProduct? Product { get; set; }
}

internal sealed class CreateSubscriptionRequest
{
    public CreateSubscriptionBody Subscription { get; set; } = new();
}

internal sealed class CreateSubscriptionBody
{
    public string ProductHandle { get; set; } = string.Empty;
    public int CustomerId { get; set; }

    /// <summary>See Collection-Method.yaml — omitted when not configured, so Maxio applies its default.</summary>
    public string? PaymentCollectionMethod { get; set; }
}

internal sealed class UpdateSubscriptionRequest
{
    public UpdateSubscriptionBody Subscription { get; set; } = new();
}

internal sealed class UpdateSubscriptionBody
{
    public string ProductHandle { get; set; } = string.Empty;

    /// <summary>Schedules the product change for the next renewal, with no proration.</summary>
    public bool ProductChangeDelayed { get; set; }
}

internal sealed class UsageEnvelope
{
    public MaxioUsage? Usage { get; set; }
}

/// <summary>Maxio's Usage. See Usage.yaml — <c>quantity</c> may arrive as a number or a string.</summary>
internal sealed class MaxioUsage
{
    public long Id { get; set; }

    [JsonConverter(typeof(FlexibleDecimalConverter))]
    public decimal Quantity { get; set; }

    public string? Memo { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int ComponentId { get; set; }
    public string? ComponentHandle { get; set; }
    public int SubscriptionId { get; set; }
}

internal sealed class CreateUsageRequest
{
    public CreateUsageBody Usage { get; set; } = new();
}

internal sealed class CreateUsageBody
{
    public decimal Quantity { get; set; }
    public string? Memo { get; set; }
}

internal sealed class MigrationRequest
{
    public MigrationBody Migration { get; set; } = new();
}

internal sealed class MigrationBody
{
    public string ProductHandle { get; set; } = string.Empty;
    public bool IncludeTrial { get; set; }
    public bool IncludeInitialCharge { get; set; }
    public bool IncludeCoupons { get; set; } = true;

    /// <summary>True keeps the billing period and issues a prorated charge — what UC3 "apply now" means.</summary>
    public bool PreservePeriod { get; set; } = true;
}

internal sealed class MigrationPreviewEnvelope
{
    public MaxioMigrationPreview? Migration { get; set; }
}

/// <summary>See Subscription-Migration-Preview.yaml — all amounts are integer cents.</summary>
internal sealed class MaxioMigrationPreview
{
    public long ProratedAdjustmentInCents { get; set; }
    public long ChargeInCents { get; set; }
    public long PaymentDueInCents { get; set; }
    public long CreditAppliedInCents { get; set; }
}

internal sealed class PauseRequest
{
    public PauseBody Hold { get; set; } = new();
}

internal sealed class PauseBody
{
    public DateTimeOffset? AutomaticallyResumeAt { get; set; }
}

internal sealed class CancellationRequest
{
    public CancellationBody Subscription { get; set; } = new();
}

internal sealed class CancellationBody
{
    public string? CancellationMessage { get; set; }
}

/// <summary>See Error-List-Response.yaml and its siblings — the errors key is polymorphic.</summary>
internal sealed class MaxioErrorResponse
{
    public IReadOnlyCollection<string>? Errors { get; set; }
    public string? Error { get; set; }
}

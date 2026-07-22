using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Services;

// Wire contracts for the Maxio Advanced Billing API, mirroring the shapes in maxio-spec/openapi.yaml.
// Property names are snake_case on the wire and are mapped by the serializer's naming policy;
// only the fields this integration actually uses are declared.

internal class MaxioProductFamilyEnvelope
{
    public MaxioProductFamily? ProductFamily { get; set; }
}

internal class MaxioProductFamily
{
    public int Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
}

internal class MaxioProductEnvelope
{
    public MaxioProduct? Product { get; set; }
}

internal class MaxioProduct
{
    public int Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }

    /// <summary>Product prices are integer cents (Product.yaml: "The product price, in integer cents").</summary>
    public int PriceInCents { get; set; }

    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public bool RequireCreditCard { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
}

internal class MaxioComponentEnvelope
{
    public MaxioComponent? Component { get; set; }
}

internal class MaxioComponent
{
    public int Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public string? Kind { get; set; }
    public string? UnitName { get; set; }
    public string? PricingScheme { get; set; }

    /// <summary>Component unit prices are decimal currency units, serialised as a string ("0.01").</summary>
    [JsonConverter(typeof(FlexibleDecimalConverter))]
    public decimal? UnitPrice { get; set; }
}

internal class MaxioCustomerEnvelope
{
    public MaxioCustomer? Customer { get; set; }
}

internal class MaxioCustomer
{
    public int Id { get; set; }
    public string? Reference { get; set; }
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}

internal class MaxioCreateCustomerRequest
{
    public MaxioCreateCustomer Customer { get; set; } = new();
}

internal class MaxioCreateCustomer
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
}

internal class MaxioSubscriptionEnvelope
{
    public MaxioSubscription? Subscription { get; set; }
}

internal class MaxioSubscription
{
    public int Id { get; set; }
    public string? State { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public bool? CancelAtEndOfPeriod { get; set; }
    public DateTimeOffset? DelayedCancelAt { get; set; }
    public DateTimeOffset? AutomaticallyResumeAt { get; set; }
    public MaxioProduct? Product { get; set; }
    public MaxioCustomer? Customer { get; set; }
}

internal class MaxioCreateSubscriptionRequest
{
    public MaxioCreateSubscription Subscription { get; set; } = new();
}

internal class MaxioCreateSubscription
{
    public string ProductHandle { get; set; } = string.Empty;
    public int CustomerId { get; set; }

    /// <summary>
    /// Collection method for the new subscription. The demo plans capture no card, so this is
    /// sent explicitly rather than relying on the provider's "automatic" default.
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }
}

internal class MaxioUpdateSubscriptionRequest
{
    public MaxioUpdateSubscription Subscription { get; set; } = new();
}

internal class MaxioUpdateSubscription
{
    public string ProductHandle { get; set; } = string.Empty;

    /// <summary>Defers the product change to the next renewal, with no proration.</summary>
    public bool ProductChangeDelayed { get; set; }
}

internal class MaxioUsageEnvelope
{
    public MaxioUsage? Usage { get; set; }
}

internal class MaxioUsage
{
    public long Id { get; set; }
    public string? Memo { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }

    [JsonConverter(typeof(FlexibleDecimalConverter))]
    public decimal? Quantity { get; set; }

    public int ComponentId { get; set; }
    public string? ComponentHandle { get; set; }
    public int SubscriptionId { get; set; }
}

internal class MaxioCreateUsageRequest
{
    public MaxioCreateUsage Usage { get; set; } = new();
}

internal class MaxioCreateUsage
{
    public decimal Quantity { get; set; }
    public string? Memo { get; set; }
}

internal class MaxioSubscriptionComponentEnvelope
{
    public MaxioSubscriptionComponent? Component { get; set; }
}

internal class MaxioSubscriptionComponent
{
    public int ComponentId { get; set; }
    public string? ComponentHandle { get; set; }
    public string? Kind { get; set; }

    /// <summary>Metered usage accrued so far in the current billing period.</summary>
    [JsonConverter(typeof(FlexibleDecimalConverter))]
    public decimal? UnitBalance { get; set; }
}

internal class MaxioMigrationPreviewEnvelope
{
    public MaxioMigrationPreview? Migration { get; set; }
}

internal class MaxioMigrationPreview
{
    public long ProratedAdjustmentInCents { get; set; }
    public long ChargeInCents { get; set; }
    public long PaymentDueInCents { get; set; }
    public long CreditAppliedInCents { get; set; }
}

internal class MaxioMigrationRequest
{
    public MaxioMigration Migration { get; set; } = new();
}

internal class MaxioMigration
{
    public string ProductHandle { get; set; } = string.Empty;
    public bool IncludeTrial { get; set; }
    public bool IncludeInitialCharge { get; set; }
    public bool IncludeCoupons { get; set; } = true;

    /// <summary>When true the billing period is kept and the difference is prorated.</summary>
    public bool PreservePeriod { get; set; } = true;
}

internal class MaxioCancellationRequest
{
    public MaxioCancellationOptions Subscription { get; set; } = new();
}

internal class MaxioCancellationOptions
{
    public string? CancellationMessage { get; set; }
}

internal class MaxioPauseRequest
{
    public MaxioPauseOptions Hold { get; set; } = new();
}

internal class MaxioPauseOptions
{
    public DateTimeOffset? AutomaticallyResumeAt { get; set; }
}

internal class MaxioReactivateRequest
{
    /// <summary>Ask the provider to resume the billing period where possible.</summary>
    public bool Resume { get; set; }
}

/// <summary>
/// The provider's error payloads. "errors" is usually an array of strings, but customer
/// validation returns an object map and cancellation can return a single "error" string.
/// </summary>
internal class MaxioErrorResponse
{
    public System.Text.Json.JsonElement? Errors { get; set; }
    public string? Error { get; set; }
}

internal static class MaxioErrorReader
{
    /// <summary>
    /// Flattens whichever error shape the provider returned into a list of messages.
    /// </summary>
    public static IReadOnlyCollection<string> Flatten(MaxioErrorResponse? response)
    {
        var messages = new List<string>();
        if (response is null)
        {
            return messages;
        }

        if (!string.IsNullOrWhiteSpace(response.Error))
        {
            messages.Add(response.Error!);
        }

        if (response.Errors is not { } errors)
        {
            return messages;
        }

        switch (errors.ValueKind)
        {
            case System.Text.Json.JsonValueKind.String:
                messages.Add(errors.GetString()!);
                break;
            case System.Text.Json.JsonValueKind.Array:
                foreach (var item in errors.EnumerateArray())
                {
                    messages.Add(item.ToString());
                }
                break;
            case System.Text.Json.JsonValueKind.Object:
                foreach (var property in errors.EnumerateObject())
                {
                    messages.Add($"{property.Name}: {property.Value}");
                }
                break;
        }

        return messages;
    }
}

using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

// Wire DTOs for the Maxio Advanced Billing API. Serialized with SnakeCaseLower naming,
// so e.g. PriceInCents <-> price_in_cents.

internal sealed class MaxioProductListItem
{
    public MaxioProduct? Product { get; set; }
}

internal sealed class MaxioProduct
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public DateTimeOffset? ArchivedAt { get; set; }
}

internal sealed class MaxioCustomerEnvelope
{
    public MaxioCustomer? Customer { get; set; }
}

internal sealed class MaxioCustomer
{
    public long Id { get; set; }
    public string? Reference { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}

internal sealed class MaxioCreateCustomerRequest
{
    public required MaxioCustomerAttributes Customer { get; set; }
}

internal sealed class MaxioCustomerAttributes
{
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public required string Email { get; set; }
    public required string Reference { get; set; }
}

internal sealed class MaxioSubscriptionListItem
{
    public MaxioSubscription? Subscription { get; set; }
}

internal sealed class MaxioSubscriptionEnvelope
{
    public MaxioSubscription? Subscription { get; set; }
}

internal sealed class MaxioSubscription
{
    public long Id { get; set; }
    public string? Reference { get; set; }
    public string State { get; set; } = string.Empty;
    public long ProductPriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public MaxioProduct? Product { get; set; }
    public MaxioCustomer? Customer { get; set; }
}

internal sealed class MaxioCreateSubscriptionRequest
{
    public required MaxioCreateSubscriptionAttributes Subscription { get; set; }

    /// <summary>
    /// Maxio duplicate-prevention token; reused across retries of the same logical
    /// operation so a retry after a timeout cannot create a duplicate.
    /// </summary>
    public required string UniquenessToken { get; set; }
}

internal sealed class MaxioCreateSubscriptionAttributes
{
    public required string ProductHandle { get; set; }
    public required long CustomerId { get; set; }
    public required string Reference { get; set; }

    /// <summary>
    /// "remittance" = invoice-based collection; the shopper is not automatically charged,
    /// so signup works without a payment method on file.
    /// </summary>
    public required string PaymentCollectionMethod { get; set; }
}

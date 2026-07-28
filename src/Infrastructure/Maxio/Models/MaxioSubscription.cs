using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

/// <summary>
/// Wire model for a Maxio <c>subscription</c>, mirroring the fields of the
/// <c>Subscription</c> schema this integration reads.
/// </summary>
public class MaxioSubscription
{
    public int Id { get; set; }

    public string? State { get; set; }

    public long ProductPriceInCents { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    public DateTimeOffset? NextAssessmentAt { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public string? PaymentCollectionMethod { get; set; }

    public MaxioCustomer? Customer { get; set; }

    public MaxioProduct? Product { get; set; }
}

/// <summary>Envelope for a single subscription, per the <c>Subscription-Response</c> schema.</summary>
public class MaxioSubscriptionResponse
{
    public MaxioSubscription? Subscription { get; set; }
}

/// <summary>Request body for creating a subscription, per the <c>Create-Subscription-Request</c> schema.</summary>
public class CreateSubscriptionRequest
{
    public CreateSubscription Subscription { get; set; } = new();
}

/// <summary>
/// The subscription attributes this integration sets on create, drawn from the
/// <c>Create-Subscription</c> schema. The plans in scope do not require a payment method,
/// so subscriptions are created with invoice-based (<c>remittance</c>) collection.
/// </summary>
public class CreateSubscription
{
    /// <summary>The API handle of the product to subscribe to.</summary>
    public string ProductHandle { get; set; } = string.Empty;

    /// <summary>The id of the existing Maxio customer to enroll.</summary>
    public int CustomerId { get; set; }

    /// <summary>
    /// Collection method. <c>remittance</c> = invoice billing (no stored payment method
    /// required); <c>automatic</c> = charge a stored payment method.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";
}

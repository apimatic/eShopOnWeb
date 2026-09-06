using System;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;

/// <summary>Wire model for the specification's <c>Subscription Response</c> schema.</summary>
public class MaxioSubscriptionResponse
{
    public MaxioSubscription? Subscription { get; set; }
}

/// <summary>Wire model for the specification's <c>Subscription</c> schema (only the fields this integration uses).</summary>
public class MaxioSubscription
{
    public int Id { get; set; }
    public string? State { get; set; }
    public string? Reference { get; set; }
    public long BalanceInCents { get; set; }
    public long ProductPriceInCents { get; set; }
    public string? Currency { get; set; }
    public string? PaymentCollectionMethod { get; set; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public MaxioCustomer? Customer { get; set; }
    public MaxioProduct? Product { get; set; }
}

/// <summary>Request body for <c>createSubscription</c>: the specification's <c>Create Subscription Request</c> schema.</summary>
public class MaxioCreateSubscriptionRequest
{
    public MaxioCreateSubscription Subscription { get; set; } = new();
}

/// <summary>Wire model for the specification's <c>Create Subscription</c> schema.</summary>
public class MaxioCreateSubscription
{
    /// <summary>API handle of the product being subscribed to.</summary>
    public string ProductHandle { get; set; } = string.Empty;

    /// <summary>Identifier of the existing Maxio customer the subscription belongs to.</summary>
    public int CustomerId { get; set; }

    /// <summary>Reference eShopOnWeb assigns to the subscription; unique within the site.</summary>
    public string? Reference { get; set; }

    /// <summary>How Maxio should collect payment for the subscription.</summary>
    public string? PaymentCollectionMethod { get; set; }
}

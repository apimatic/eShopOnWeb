using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

// Wire models for the Maxio Advanced Billing API. They serialize with a snake_case
// naming policy, so PriceInCents maps to "price_in_cents", and so on.

public class MaxioProduct
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
}

public class MaxioProductResponse
{
    public MaxioProduct? Product { get; set; }
}

public class MaxioCustomer
{
    public int Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

public class MaxioCustomerResponse
{
    public MaxioCustomer? Customer { get; set; }
}

public class MaxioCustomerRequest
{
    public MaxioCustomer Customer { get; set; } = new MaxioCustomer();
}

public class MaxioSubscription
{
    public int Id { get; set; }
    public string? State { get; set; }
    public string? Reference { get; set; }
    public long ProductPriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public MaxioProduct? Product { get; set; }
    public MaxioCustomer? Customer { get; set; }
}

public class MaxioSubscriptionResponse
{
    public MaxioSubscription? Subscription { get; set; }
}

public class MaxioSubscriptionRequest
{
    public MaxioSubscriptionRequestItem Subscription { get; set; } = new MaxioSubscriptionRequestItem();
}

public class MaxioSubscriptionRequestItem
{
    public string? ProductHandle { get; set; }
    public string? CustomerReference { get; set; }
    public string? Reference { get; set; }

    /// <summary>"automatic" charges a card on file; "remittance" invoices instead.</summary>
    public string? PaymentCollectionMethod { get; set; }
}

// Application-level models returned by the billing service.

public record SubscriptionPlanModel(
    string Name,
    string Handle,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    bool RequiresPaymentMethod);

public record SubscriptionModel(
    int SubscriptionId,
    string State,
    string PlanName,
    string PlanHandle,
    long PriceInCents,
    DateTimeOffset? NextBillingDate,
    DateTimeOffset? ActivatedAt);

public record SubscribeResultModel(SubscriptionModel Subscription, bool AlreadyExisted);

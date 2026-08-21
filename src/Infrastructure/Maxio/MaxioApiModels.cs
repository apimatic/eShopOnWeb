using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

internal sealed class ProductEnvelope
{
    public ProductPayload? Product { get; set; }
}

internal sealed class ProductPayload
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public ProductFamilyPayload? ProductFamily { get; set; }
}

internal sealed class ProductFamilyPayload
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
}

internal sealed class CustomerEnvelope
{
    public CustomerPayload? Customer { get; set; }
}

internal sealed class CustomerPayload
{
    public int Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

internal sealed class CreateCustomerRequestBody
{
    public CreateCustomerPayload Customer { get; set; } = new();
    public string? UniquenessToken { get; set; }
}

internal sealed class CreateCustomerPayload
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
}

internal sealed class SubscriptionEnvelope
{
    public SubscriptionPayload? Subscription { get; set; }
}

internal sealed class SubscriptionPayload
{
    public int Id { get; set; }
    public string? State { get; set; }
    public long ProductPriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public string? Reference { get; set; }
    public ProductPayload? Product { get; set; }
}

internal sealed class CreateSubscriptionRequestBody
{
    public CreateSubscriptionPayload Subscription { get; set; } = new();
    public string? UniquenessToken { get; set; }
}

internal sealed class CreateSubscriptionPayload
{
    public string ProductHandle { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public string Reference { get; set; } = string.Empty;
    // Relationship Invoicing: remittance creates the subscription and invoices without capturing a card.
    public string PaymentCollectionMethod { get; set; } = "remittance";
}

internal sealed class MaxioErrorBody
{
    [JsonPropertyName("errors")]
    public object? Errors { get; set; }
}

internal static class MaxioMappings
{
    public static SubscriptionPlan ToPlan(ProductPayload product)
    {
        return new SubscriptionPlan(
            Handle: product.Handle ?? string.Empty,
            Name: product.Name ?? string.Empty,
            Description: product.Description,
            Price: product.PriceInCents / 100m,
            PriceInCents: product.PriceInCents,
            Interval: product.Interval,
            IntervalUnit: product.IntervalUnit ?? string.Empty);
    }

    public static CustomerSubscription ToSubscription(SubscriptionPayload subscription)
    {
        var nextBilling = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt;
        return new CustomerSubscription(
            Id: subscription.Id,
            State: subscription.State ?? string.Empty,
            ProductHandle: subscription.Product?.Handle,
            ProductName: subscription.Product?.Name,
            Price: subscription.ProductPriceInCents / 100m,
            PriceInCents: subscription.ProductPriceInCents,
            NextBillingDate: nextBilling,
            Reference: subscription.Reference);
    }
}

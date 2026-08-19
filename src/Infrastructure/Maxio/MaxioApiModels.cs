using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.Infrastructure.Maxio;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

/// <summary>
/// Request/response shapes that match the Maxio Advanced Billing OpenAPI spec.
/// </summary>
internal sealed class ProductResponse
{
    public ProductDto? Product { get; set; }
}

internal sealed class ProductDto
{
    public int? Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long? PriceInCents { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public bool? RequireCreditCard { get; set; }
    public ProductFamilyDto? ProductFamily { get; set; }
}

internal sealed class ProductFamilyDto
{
    public int? Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
}

internal sealed class CustomerResponse
{
    public CustomerDto? Customer { get; set; }
}

internal sealed class CustomerDto
{
    public int? Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

internal sealed class CreateCustomerRequest
{
    public CreateCustomerDto Customer { get; set; } = new();
}

internal sealed class CreateCustomerDto
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
}

internal sealed class SubscriptionResponse
{
    public SubscriptionDto? Subscription { get; set; }
}

internal sealed class SubscriptionDto
{
    public int? Id { get; set; }
    public string? State { get; set; }
    public string? Reference { get; set; }
    public long? ProductPriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public CustomerDto? Customer { get; set; }
    public ProductDto? Product { get; set; }
}

internal sealed class CreateSubscriptionRequest
{
    public CreateSubscriptionDto Subscription { get; set; } = new();
}

internal sealed class CreateSubscriptionDto
{
    public string ProductHandle { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string PaymentCollectionMethod { get; set; } = string.Empty;
}

internal sealed class ErrorListResponse
{
    public object? Errors { get; set; }
}

internal static class MaxioMapping
{
    public static ApplicationCore.Subscriptions.SubscriptionPlan ToPlan(ProductDto product)
    {
        return new ApplicationCore.Subscriptions.SubscriptionPlan
        {
            Id = product.Id ?? 0,
            Handle = product.Handle ?? string.Empty,
            Name = product.Name ?? string.Empty,
            Description = product.Description,
            Price = CentsToDecimal(product.PriceInCents),
            Interval = product.Interval ?? 1,
            IntervalUnit = product.IntervalUnit ?? "month",
            ProductFamilyHandle = product.ProductFamily?.Handle,
            RequireCreditCard = product.RequireCreditCard ?? false
        };
    }

    public static ApplicationCore.Subscriptions.BillingCustomer ToCustomer(CustomerDto customer)
    {
        return new ApplicationCore.Subscriptions.BillingCustomer
        {
            Id = customer.Id ?? 0,
            Reference = customer.Reference,
            Email = customer.Email ?? string.Empty,
            FirstName = customer.FirstName ?? string.Empty,
            LastName = customer.LastName ?? string.Empty
        };
    }

    public static ApplicationCore.Subscriptions.ShopperSubscription ToSubscription(SubscriptionDto subscription)
    {
        return new ApplicationCore.Subscriptions.ShopperSubscription
        {
            Id = subscription.Id ?? 0,
            Reference = subscription.Reference,
            State = subscription.State ?? string.Empty,
            ProductHandle = subscription.Product?.Handle ?? string.Empty,
            ProductName = subscription.Product?.Name ?? string.Empty,
            Price = CentsToDecimal(subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents),
            NextBillingDate = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
            CustomerId = subscription.Customer?.Id
        };
    }

    public static decimal CentsToDecimal(long? cents) =>
        cents is null ? 0m : cents.Value / 100m;
}

internal static class MaxioErrorFormatter
{
    public static string Format(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "Maxio Advanced Billing returned an error with an empty body.";
        }

        try
        {
            var parsed = System.Text.Json.JsonSerializer.Deserialize<ErrorListResponse>(body, MaxioJson.SerializerOptions);
            if (parsed?.Errors is System.Text.Json.JsonElement element)
            {
                if (element.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    var messages = new List<string>();
                    foreach (var item in element.EnumerateArray())
                    {
                        if (item.ValueKind == System.Text.Json.JsonValueKind.String)
                        {
                            messages.Add(item.GetString() ?? string.Empty);
                        }
                    }

                    if (messages.Count > 0)
                    {
                        return string.Join(" ", messages);
                    }
                }
                else if (element.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    var messages = new List<string>();
                    foreach (var property in element.EnumerateObject())
                    {
                        messages.Add($"{property.Name}: {property.Value}");
                    }

                    if (messages.Count > 0)
                    {
                        return string.Join(" ", messages);
                    }
                }
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Fall through and return the raw body.
        }

        return body.Length <= 500 ? body : body[..500];
    }
}

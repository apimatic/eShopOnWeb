using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

internal static class MaxioContracts
{
    internal sealed class CustomerResponse
    {
        public Customer? Customer { get; set; }
    }

    internal sealed class Customer
    {
        public int Id { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Email { get; set; }
        public string? Reference { get; set; }
    }

    internal sealed class CreateCustomerRequest
    {
        public CreateCustomer Customer { get; set; } = new();
    }

    internal sealed class CreateCustomer
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Reference { get; set; } = string.Empty;
    }

    internal sealed class ProductResponse
    {
        public Product? Product { get; set; }
    }

    internal sealed class Product
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Handle { get; set; }
        public string? Description { get; set; }
        public int PriceInCents { get; set; }
        public int Interval { get; set; }
        public string? IntervalUnit { get; set; }
        public DateTimeOffset? ArchivedAt { get; set; }
        public bool RequireCreditCard { get; set; }
        public ProductFamily? ProductFamily { get; set; }
    }

    internal sealed class ProductFamily
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Handle { get; set; }
    }

    internal sealed class SubscriptionResponse
    {
        public Subscription? Subscription { get; set; }
    }

    internal sealed class Subscription
    {
        public int Id { get; set; }
        public string? Reference { get; set; }
        public string? State { get; set; }
        public long ProductPriceInCents { get; set; }
        public DateTimeOffset? NextAssessmentAt { get; set; }
        public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
        public Product? Product { get; set; }
        public Customer? Customer { get; set; }
    }

    internal sealed class CreateSubscriptionRequest
    {
        public CreateSubscription Subscription { get; set; } = new();
    }

    internal sealed class CreateSubscription
    {
        public string? ProductHandle { get; set; }
        public int? CustomerId { get; set; }
        public string? Reference { get; set; }
        public string? PaymentCollectionMethod { get; set; }
    }

    internal static SubscriptionPlan ToPlan(Product product)
    {
        return new SubscriptionPlan
        {
            Id = product.Id,
            Handle = product.Handle ?? string.Empty,
            Name = product.Name ?? string.Empty,
            Description = product.Description,
            PriceInCents = product.PriceInCents,
            Interval = product.Interval,
            IntervalUnit = product.IntervalUnit ?? string.Empty,
            ProductFamilyHandle = product.ProductFamily?.Handle,
            RequireCreditCard = product.RequireCreditCard
        };
    }

    internal static BillingCustomer ToCustomer(Customer customer)
    {
        return new BillingCustomer
        {
            Id = customer.Id,
            Reference = customer.Reference,
            Email = customer.Email ?? string.Empty,
            FirstName = customer.FirstName ?? string.Empty,
            LastName = customer.LastName ?? string.Empty
        };
    }

    internal static ShopperSubscription ToShopperSubscription(Subscription subscription)
    {
        return new ShopperSubscription
        {
            Id = subscription.Id,
            Reference = subscription.Reference,
            State = subscription.State ?? string.Empty,
            ProductPriceInCents = checked((int)subscription.ProductPriceInCents),
            NextAssessmentAt = subscription.NextAssessmentAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            ProductHandle = subscription.Product?.Handle,
            ProductName = subscription.Product?.Name,
            CustomerId = subscription.Customer?.Id
        };
    }

    internal static MaxioApiException ToApiException(int statusCode, string body)
    {
        var errors = ParseErrors(body);
        var message = errors.Count > 0
            ? string.Join(" ", errors)
            : $"Maxio API request failed with status {statusCode}.";
        return new MaxioApiException(message, statusCode, body, errors);
    }

    private static IReadOnlyList<string> ParseErrors(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("errors", out var errors))
            {
                return Array.Empty<string>();
            }

            if (errors.ValueKind == JsonValueKind.Array)
            {
                return errors.EnumerateArray()
                    .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() ?? item.GetRawText() : item.GetRawText())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();
            }

            if (errors.ValueKind == JsonValueKind.Object)
            {
                return errors.EnumerateObject()
                    .Select(property => $"{property.Name}: {property.Value.ToString()}")
                    .ToList();
            }

            if (errors.ValueKind == JsonValueKind.String)
            {
                var value = errors.GetString();
                return string.IsNullOrWhiteSpace(value) ? Array.Empty<string>() : new[] { value };
            }
        }
        catch (JsonException)
        {
            // Fall through and return no structured errors.
        }

        return Array.Empty<string>();
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.eShopWeb.ApplicationCore.Entities.Billing;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

// Request/response envelopes matching maxio-spec/components/schemas.

internal sealed class ProductResponse
{
    public ProductDto? Product { get; set; }
}

internal sealed class ProductDto
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public long? TrialPriceInCents { get; set; }
    public bool RequireCreditCard { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public ProductFamilyDto? ProductFamily { get; set; }

    public SubscriptionPlan ToPlan() => new()
    {
        Id = Id,
        Handle = Handle ?? string.Empty,
        Name = Name ?? string.Empty,
        Description = Description,
        PriceInCents = PriceInCents,
        Interval = Interval,
        IntervalUnit = IntervalUnit ?? string.Empty,
        TrialPriceInCents = TrialPriceInCents,
        RequireCreditCard = RequireCreditCard,
        ProductFamilyHandle = ProductFamily?.Handle
    };
}

internal sealed class ProductFamilyDto
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
}

internal sealed class CustomerResponse
{
    public CustomerDto? Customer { get; set; }
}

internal sealed class CustomerDto
{
    public int Id { get; set; }
    public string? Reference { get; set; }
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }

    public MaxioCustomer ToCustomer() => new()
    {
        Id = Id,
        Reference = Reference,
        Email = Email ?? string.Empty,
        FirstName = FirstName ?? string.Empty,
        LastName = LastName ?? string.Empty
    };
}

internal sealed class CreateCustomerRequest
{
    public CreateCustomer? Customer { get; set; }
}

internal sealed class CreateCustomer
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Reference { get; set; }
}

internal sealed class SubscriptionResponse
{
    public SubscriptionDto? Subscription { get; set; }
}

internal sealed class SubscriptionDto
{
    public int Id { get; set; }
    public string? State { get; set; }
    public string? Reference { get; set; }
    public long ProductPriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public CustomerDto? Customer { get; set; }
    public ProductDto? Product { get; set; }

    public CustomerSubscription ToSubscription() => new()
    {
        Id = Id,
        State = State ?? string.Empty,
        Reference = Reference,
        CustomerId = Customer?.Id,
        ProductHandle = Product?.Handle ?? string.Empty,
        ProductName = Product?.Name ?? string.Empty,
        PriceInCents = ProductPriceInCents != 0 ? ProductPriceInCents : Product?.PriceInCents ?? 0,
        Interval = Product?.Interval ?? 0,
        IntervalUnit = Product?.IntervalUnit ?? string.Empty,
        CurrentPeriodEndsAt = CurrentPeriodEndsAt,
        NextBillingDate = NextAssessmentAt ?? CurrentPeriodEndsAt,
        CreatedAt = CreatedAt,
        ActivatedAt = ActivatedAt
    };
}

internal sealed class CreateSubscriptionRequest
{
    public CreateSubscription? Subscription { get; set; }
}

internal sealed class CreateSubscription
{
    public string? ProductHandle { get; set; }
    public int? CustomerId { get; set; }
    public string? Reference { get; set; }
    public string? PaymentCollectionMethod { get; set; }
}

internal static class MaxioErrorParser
{
    public static IReadOnlyList<string> Parse(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (!document.RootElement.TryGetProperty("errors", out var errors))
            {
                return Array.Empty<string>();
            }

            return errors.ValueKind switch
            {
                JsonValueKind.Array => errors.EnumerateArray()
                    .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.ToString())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Cast<string>()
                    .ToList(),
                JsonValueKind.Object => errors.EnumerateObject()
                    .Select(p => $"{p.Name}: {p.Value}")
                    .ToList(),
                JsonValueKind.String => new[] { errors.GetString()! },
                _ => Array.Empty<string>()
            };
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }
}

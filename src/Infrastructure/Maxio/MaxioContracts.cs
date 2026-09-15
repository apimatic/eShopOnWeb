using System;
using System.Text.Json;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public sealed class ProductResponse
{
    public ProductPayload? Product { get; set; }
}

public sealed class ProductPayload
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
    public ProductFamilyPayload? ProductFamily { get; set; }
}

public sealed class ProductFamilyResponse
{
    public ProductFamilyPayload? ProductFamily { get; set; }
}

public sealed class ProductFamilyPayload
{
    public int? Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
}

public sealed class CustomerResponse
{
    public CustomerPayload? Customer { get; set; }
}

public sealed class CustomerPayload
{
    public int? Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

public sealed class CreateCustomerRequest
{
    public CreateCustomerPayload Customer { get; set; } = new();
}

public sealed class CreateCustomerPayload
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
}

public sealed class SubscriptionResponse
{
    public SubscriptionPayload? Subscription { get; set; }
}

public sealed class SubscriptionPayload
{
    public int? Id { get; set; }
    public string? State { get; set; }
    public long? ProductPriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public string? Reference { get; set; }
    public ProductPayload? Product { get; set; }
    public CustomerPayload? Customer { get; set; }
}

public sealed class CreateSubscriptionRequest
{
    public CreateSubscriptionPayload Subscription { get; set; } = new();
}

public sealed class CreateSubscriptionPayload
{
    public string ProductHandle { get; set; } = string.Empty;
    public int? CustomerId { get; set; }
    public string? Reference { get; set; }
    public string? PaymentCollectionMethod { get; set; }
}

internal sealed class ErrorListResponse
{
    public JsonElement Errors { get; set; }
}

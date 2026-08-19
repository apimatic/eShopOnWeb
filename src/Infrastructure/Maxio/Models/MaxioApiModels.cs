using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

internal sealed class ProductResponse
{
    public ProductPayload Product { get; set; } = new();
}

internal sealed class ProductPayload
{
    public int Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public bool RequireCreditCard { get; set; }
    public ProductFamilyPayload? ProductFamily { get; set; }
}

internal sealed class ProductFamilyPayload
{
    public int Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
}

internal sealed class CustomerResponse
{
    public CustomerPayload Customer { get; set; } = new();
}

internal sealed class CustomerPayload
{
    public int Id { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}

internal sealed class CreateCustomerRequest
{
    public CreateCustomerPayload Customer { get; set; } = new();
}

internal sealed class CreateCustomerPayload
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Reference { get; set; }
}

internal sealed class UpdateCustomerRequest
{
    public UpdateCustomerPayload Customer { get; set; } = new();
}

internal sealed class UpdateCustomerPayload
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

internal sealed class SubscriptionResponse
{
    public SubscriptionPayload? Subscription { get; set; }
}

internal sealed class SubscriptionPayload
{
    public int Id { get; set; }
    public string? State { get; set; }
    public long ProductPriceInCents { get; set; }
    public string? Reference { get; set; }
    public string? NextAssessmentAt { get; set; }
    public string? CurrentPeriodStartedAt { get; set; }
    public string? CurrentPeriodEndsAt { get; set; }
    public string? CreatedAt { get; set; }
    public CustomerPayload? Customer { get; set; }
    public ProductPayload? Product { get; set; }
}

internal sealed class CreateSubscriptionRequest
{
    public CreateSubscriptionPayload Subscription { get; set; } = new();
}

internal sealed class CreateSubscriptionPayload
{
    public string ProductHandle { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public string? Reference { get; set; }
    public string PaymentCollectionMethod { get; set; } = "remittance";
}

internal sealed class ErrorListResponse
{
    [JsonConverter(typeof(MaxioErrorListConverter))]
    public List<string> Errors { get; set; } = new();
}

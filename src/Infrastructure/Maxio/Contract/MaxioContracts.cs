using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Contract;

internal sealed class CustomerResponse
{
    public CustomerResource? Customer { get; set; }
}

internal sealed class CustomerResource
{
    public int Id { get; set; }
    public string? Reference { get; set; }
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
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
    public ProductResource? Product { get; set; }
}

internal sealed class ProductResource
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public bool RequireCreditCard { get; set; }
    public string? ArchivedAt { get; set; }
    public ProductFamilyResource? ProductFamily { get; set; }
}

internal sealed class ProductFamilyResource
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
}

internal sealed class SubscriptionResponse
{
    public SubscriptionResource? Subscription { get; set; }
}

internal sealed class SubscriptionResource
{
    public int Id { get; set; }
    public string? State { get; set; }
    public long ProductPriceInCents { get; set; }
    public string? CurrentPeriodEndsAt { get; set; }
    public string? NextAssessmentAt { get; set; }
    public string? Reference { get; set; }
    public CustomerResource? Customer { get; set; }
    public ProductResource? Product { get; set; }
}

internal sealed class CreateSubscriptionRequest
{
    public CreateSubscription Subscription { get; set; } = new();
}

internal sealed class CreateSubscription
{
    public string ProductHandle { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public string Reference { get; set; } = string.Empty;

    [JsonPropertyName("payment_collection_method")]
    public string? PaymentCollectionMethod { get; set; }
}

internal sealed class ErrorListResponse
{
    public object? Errors { get; set; }
}

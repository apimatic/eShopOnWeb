using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

public sealed class MaxioProductFamily
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
}

public sealed class MaxioProduct
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public bool RequireCreditCard { get; set; }
    public DateTime? ArchivedAt { get; set; }
    public MaxioProductFamily ProductFamily { get; set; } = new();
}

public sealed class MaxioCustomer
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Reference { get; set; }
}

public sealed class MaxioSubscription
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public int ProductPriceInCents { get; set; }
    public string? PaymentCollectionMethod { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? CurrentPeriodStartedAt { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? NextAssessmentAt { get; set; }
    public DateTime? CanceledAt { get; set; }
    public MaxioProduct Product { get; set; } = new();
    public MaxioCustomer Customer { get; set; } = new();
}

public sealed class MaxioCreateCustomerRequest
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
}

public sealed class MaxioCreateSubscriptionRequest
{
    public string ProductHandle { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public string? Reference { get; set; }
    public string PaymentCollectionMethod { get; set; } = "remittance";
}

public sealed class MaxioProductResponse { public MaxioProduct Product { get; set; } = new(); }
public sealed class MaxioCustomerResponse { public MaxioCustomer Customer { get; set; } = new(); }
public sealed class MaxioSubscriptionResponse { public MaxioSubscription Subscription { get; set; } = new(); }

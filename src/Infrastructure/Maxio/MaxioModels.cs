using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class MaxioProduct
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public bool RequireCreditCard { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
}

public class MaxioCustomer
{
    public int Id { get; set; }
    public string? Reference { get; set; }
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}

public class MaxioSubscription
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string PaymentCollectionMethod { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public int BalanceInCents { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public MaxioProduct Product { get; set; } = new();
}

public class MaxioCustomerCreateRequest
{
    public string Reference { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
}

internal sealed class ProductFamily
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

internal sealed class ProductFamilyWrapper
{
    public ProductFamily ProductFamily { get; set; } = new();
}

internal sealed class ProductWrapper
{
    public MaxioProduct Product { get; set; } = new();
}

internal sealed class CustomerWrapper
{
    public MaxioCustomer? Customer { get; set; }
}

internal sealed class SubscriptionWrapper
{
    public MaxioSubscription? Subscription { get; set; }
}

internal sealed class CreateSubscriptionPayload
{
    public int CustomerId { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string PaymentCollectionMethod { get; set; } = "remittance";
}

internal sealed class CreateSubscriptionEnvelope
{
    public CreateSubscriptionPayload Subscription { get; set; } = new();
}

internal sealed class MaxioErrorEnvelope
{
    public List<string>? Errors { get; set; }
    public Dictionary<string, List<string>>? FieldErrors { get; set; }
}

using System;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

#region Requests

public class CreateCustomerRequest
{
    public CustomerData Customer { get; set; } = new();
}

public class CustomerData
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Reference { get; set; }
}

public class CreateSubscriptionRequest
{
    public SubscriptionData Subscription { get; set; } = new();
}

public class SubscriptionData
{
    public string ProductHandle { get; set; } = string.Empty;
    public int? CustomerId { get; set; }
    public string? CustomerReference { get; set; }
}

#endregion

#region Responses

public class CustomerResponse
{
    public CustomerDto? Customer { get; set; }
}

public class CustomerDto
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class SubscriptionResponse
{
    public SubscriptionDto? Subscription { get; set; }
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public int? ProductId { get; set; }
    public string? ProductHandle { get; set; }
    public int? ProductPricePointId { get; set; }
    public string State { get; set; } = string.Empty;
    public int BalanceInCents { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public SubscriptionProductDto? Product { get; set; }
}

public class SubscriptionProductDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public int PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
}

public class SubscriptionsResponse
{
    public SubscriptionDto[]? Subscriptions { get; set; }
}

public class ProductResponse
{
    public ProductDto? Product { get; set; }
}

public class ProductDto
{
    public int Id { get; set; }
    public int ProductFamilyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class ProductsResponse
{
    public ProductDto[]? Products { get; set; }
}

#endregion

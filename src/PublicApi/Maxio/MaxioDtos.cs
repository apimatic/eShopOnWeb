using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class ProductDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = "month";

    public decimal GetPrice() => PriceInCents / 100m;
}

public class CustomerDto
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public DateTime? CreatedAt { get; set; }

    public decimal GetPrice() => PriceInCents / 100m;
}

public class CreateCustomerRequest
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
}

public class CreateSubscriptionRequest
{
    public string ProductHandle { get; set; } = string.Empty;
    public int? CustomerId { get; set; }
    public object? CustomerAttributes { get; set; }
}

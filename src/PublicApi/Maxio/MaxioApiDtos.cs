using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class ProductDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public int Price_in_cents { get; set; }
    public int Interval { get; set; }
    public string Interval_unit { get; set; } = "month";
}

public class ProductListResponse
{
    public List<ProductDto> Items { get; set; } = new();
}

public class CustomerDto
{
    public int Id { get; set; }
    public string First_name { get; set; } = string.Empty;
    public string Last_name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public DateTime Created_at { get; set; }
    public DateTime Updated_at { get; set; }
}

public class CustomerResponse
{
    public CustomerDto Customer { get; set; } = new();
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public int Product_price_in_cents { get; set; }
    public DateTime? Current_period_ends_at { get; set; }
    public DateTime? Next_assessment_at { get; set; }
    public DateTime Created_at { get; set; }
    public DateTime Updated_at { get; set; }
    public ProductDto? Product { get; set; }
    public CustomerDto? Customer { get; set; }
}

public class SubscriptionResponse
{
    public SubscriptionDto Subscription { get; set; } = new();
}

public class SubscriptionListResponse
{
    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}

public class CreateCustomerRequest
{
    public CustomerCreateDto Customer { get; set; } = new();
}

public class CustomerCreateDto
{
    public string First_name { get; set; } = string.Empty;
    public string Last_name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Reference { get; set; }
}

public class CreateSubscriptionRequest
{
    public SubscriptionCreateDto Subscription { get; set; } = new();
}

public class SubscriptionCreateDto
{
    public string? Product_handle { get; set; }
    public int? Customer_id { get; set; }
    public string? Customer_reference { get; set; }
    public CustomerAttributesDto? Customer_attributes { get; set; }
}

public class CustomerAttributesDto
{
    public string First_name { get; set; } = string.Empty;
    public string Last_name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Reference { get; set; }
}

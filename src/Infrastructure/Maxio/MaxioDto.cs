using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class ProductResponse
{
    public Product Product { get; set; } = new();
}

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public long Price_in_cents { get; set; }
    public int Interval { get; set; }
    public string Interval_unit { get; set; } = string.Empty;
    public ProductFamily Product_family { get; set; } = new();
    public bool Request_credit_card { get; set; }
    public bool Require_credit_card { get; set; }
}

public class ProductFamily
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
}

public class SubscriptionResponse
{
    public Subscription Subscription { get; set; } = new();
}

public class Subscription
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public long Balance_in_cents { get; set; }
    public DateTime? Current_period_ends_at { get; set; }
    public DateTime? Next_assessment_at { get; set; }
    public DateTime? Activated_at { get; set; }
    public DateTime Created_at { get; set; }
    public DateTime Updated_at { get; set; }
    public Customer Customer { get; set; } = new();
    public Product Product { get; set; } = new();
    public long? Product_price_in_cents { get; set; }
}

public class Customer
{
    public int Id { get; set; }
    public string First_name { get; set; } = string.Empty;
    public string Last_name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Reference { get; set; }
}

public class CustomerCreateRequest
{
    public CustomerAttributes Customer { get; set; } = new();
}

public class CustomerAttributes
{
    public string First_name { get; set; } = string.Empty;
    public string Last_name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Reference { get; set; }
}

public class SubscriptionCreateRequest
{
    public SubscriptionCreate Subscription { get; set; } = new();
}

public class SubscriptionCreate
{
    public string? Product_handle { get; set; }
    public int? Product_id { get; set; }
    public int? Customer_id { get; set; }
    public string? Customer_reference { get; set; }
    public CustomerAttributes? Customer_attributes { get; set; }
    public string? Payment_collection_method { get; set; } = "remittance";
}

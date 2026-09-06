using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Services;

public class MaxioCustomerRequest
{
    public Customer customer { get; set; } = new();
}

public class Customer
{
    public string first_name { get; set; } = "";
    public string last_name { get; set; } = "";
    public string email { get; set; } = "";
    public string reference { get; set; } = "";
}

public class MaxioCustomerResponse
{
    public CustomerData customer { get; set; } = new();
}

public class CustomerData
{
    public int id { get; set; }
    public string first_name { get; set; } = "";
    public string last_name { get; set; } = "";
    public string email { get; set; } = "";
    public string reference { get; set; } = "";
    public DateTime created_at { get; set; }
    public DateTime updated_at { get; set; }
}

public class MaxioProductsResponse
{
    public List<ProductData> products { get; set; } = new();
}

public class ProductData
{
    public int id { get; set; }
    public string name { get; set; } = "";
    public string handle { get; set; } = "";
    public string description { get; set; } = "";
    public long price_in_cents { get; set; }
    public int interval { get; set; }
    public string interval_unit { get; set; } = "";
}

public class MaxioCreateSubscriptionRequest
{
    public CreateSubscriptionData subscription { get; set; } = new();
}

public class CreateSubscriptionData
{
    public string product_handle { get; set; } = "";
    public int customer_id { get; set; }
    public string payment_collection_method { get; set; } = "remittance";
}

public class MaxioSubscriptionResponse
{
    public SubscriptionData subscription { get; set; } = new();
}

public class SubscriptionData
{
    public int id { get; set; }
    public string state { get; set; } = "";
    public int customer_id { get; set; }
    public DateTime? activated_at { get; set; }
    public DateTime? canceled_at { get; set; }
    public DateTime current_period_starts_at { get; set; }
    public DateTime current_period_ends_at { get; set; }
    public DateTime next_assessment_at { get; set; }
    public ProductData product { get; set; } = new();
    public long product_price_in_cents { get; set; }
}

public class MaxioSubscriptionsListResponse
{
    public List<SubscriptionData> subscriptions { get; set; } = new();
}

public class MaxioErrorResponse
{
    public List<string> errors { get; set; } = new();
}

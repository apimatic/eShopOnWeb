using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

// Response DTOs mirror the Maxio Advanced Billing JSON contract (snake_case).
// Serialization uses JsonNamingPolicy.SnakeCaseLower so C# PascalCase maps to snake_case.

public class MaxioCustomerResponse
{
    public MaxioCustomer Customer { get; set; } = new();
}

public class MaxioCustomer
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public string? Organization { get; set; }
}

public class MaxioProductFamilyResponse
{
    public MaxioProductFamily ProductFamily { get; set; } = new();
}

public class MaxioProductFamily
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class MaxioProductResponse
{
    public MaxioProduct Product { get; set; } = new();
}

public class MaxioProduct
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public bool Taxable { get; set; }
    public bool RequireCreditCard { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public MaxioProductFamily ProductFamily { get; set; } = new();
}

public class MaxioSubscriptionResponse
{
    public MaxioSubscription Subscription { get; set; } = new();
}

public class MaxioSubscription
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public int ProductPriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public string PaymentCollectionMethod { get; set; } = string.Empty;
    public MaxioProduct Product { get; set; } = new();
    public MaxioCustomer Customer { get; set; } = new();
}

public class MaxioErrorResponse
{
    public List<string> Errors { get; set; } = new();
}

// Request DTOs

public class CreateCustomerRequest
{
    public CreateCustomerRequest(string firstName, string lastName, string email, string reference)
    {
        Customer = new CreateCustomerBody
        {
            FirstName = firstName,
            LastName = lastName,
            Email = email,
            Reference = reference
        };
    }

    public CreateCustomerBody Customer { get; set; }

    public class CreateCustomerBody
    {
        [JsonPropertyName("first_name")]
        public string FirstName { get; set; } = string.Empty;

        [JsonPropertyName("last_name")]
        public string LastName { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public string Reference { get; set; } = string.Empty;
    }
}

public class CreateSubscriptionRequest
{
    public CreateSubscriptionRequest(int customerId, string productHandle)
    {
        Subscription = new CreateSubscriptionBody
        {
            CustomerId = customerId,
            ProductHandle = productHandle,
            PaymentCollectionMethod = "remittance"
        };
    }

    public CreateSubscriptionBody Subscription { get; set; }

    public class CreateSubscriptionBody
    {
        [JsonPropertyName("customer_id")]
        public int CustomerId { get; set; }

        [JsonPropertyName("product_handle")]
        public string ProductHandle { get; set; } = string.Empty;

        [JsonPropertyName("payment_collection_method")]
        public string PaymentCollectionMethod { get; set; } = "remittance";
    }
}

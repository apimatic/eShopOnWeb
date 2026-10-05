using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

// NOTE: All JSON shapes below mirror maxio-spec/openapi.yaml exactly
// (Product-Response, Customer-Response, Subscription-Response,
// Create-Customer-Request, Create-Subscription-Request, errors/Error-List-Response).
// Serialization uses a snake_case naming policy, matching the spec's field names.

/// <summary>Wrapper for a single Maxio product (Product-Response).</summary>
public class MaxioProductResponse
{
    public MaxioProduct Product { get; set; } = new();
}

/// <summary>Wrapper for a single Maxio customer (Customer-Response).</summary>
public class MaxioCustomerResponse
{
    public MaxioCustomer Customer { get; set; } = new();
}

/// <summary>Wrapper for a single Maxio subscription (Subscription-Response).</summary>
public class MaxioSubscriptionResponse
{
    public MaxioSubscription Subscription { get; set; } = new();
}

/// <summary>Product resource (components/schemas/Product.yaml subset used by the app).</summary>
public class MaxioProduct
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public string? ArchivedAt { get; set; }
    public bool Taxable { get; set; }
    public bool RequireCreditCard { get; set; }
    public string? ProductPricePointName { get; set; }
    public MaxioProductFamilyRef? ProductFamily { get; set; }
}

public class MaxioProductFamilyRef
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Handle { get; set; }
}

/// <summary>Customer resource (components/schemas/Customer.yaml subset used by the app).</summary>
public class MaxioCustomer
{
    public int Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
    public string? Organization { get; set; }
    public string? CreatedAt { get; set; }
    public string? UpdatedAt { get; set; }
}

/// <summary>Body for POST /customers.json (Create-Customer-Request).</summary>
public class MaxioCreateCustomerRequest
{
    public MaxioCreateCustomer Customer { get; set; } = new();
}

/// <summary>Customer attributes (components/schemas/Create-Customer.yaml subset).</summary>
public class MaxioCreateCustomer
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    /// <summary>The reference value (provided by your app) - our stable user id.</summary>
    public string Reference { get; set; } = string.Empty;
    public string? Organization { get; set; }
}

/// <summary>Subscription resource (components/schemas/Subscription.yaml subset used by the app).</summary>
public class MaxioSubscription
{
    public int Id { get; set; }
    public string? State { get; set; }
    public long BalanceInCents { get; set; }
    public long ProductPriceInCents { get; set; }
    public string? CurrentPeriodEndsAt { get; set; }
    public string? NextAssessmentAt { get; set; }
    public string? CurrentPeriodStartedAt { get; set; }
    public string? ActivatedAt { get; set; }
    public string? CreatedAt { get; set; }
    public string? CanceledAt { get; set; }
    public bool? CancelAtEndOfPeriod { get; set; }
    public string? DelayedCancelAt { get; set; }
    public string? Reference { get; set; }
    public MaxioSubscriptionCustomer? Customer { get; set; }
    public MaxioSubscriptionProduct? Product { get; set; }
}

/// <summary>Customer object embedded in a subscription response.</summary>
public class MaxioSubscriptionCustomer
{
    public int Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

/// <summary>Product object embedded in a subscription response.</summary>
public class MaxioSubscriptionProduct
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Handle { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public MaxioProductFamilyRef? ProductFamily { get; set; }
}

/// <summary>Body for POST /subscriptions.json (Create-Subscription-Request).</summary>
public class MaxioCreateSubscriptionRequest
{
    public MaxioCreateSubscription Subscription { get; set; } = new();
}

/// <summary>Subscription attributes (components/schemas/Create-Subscription.yaml subset).</summary>
public class MaxioCreateSubscription
{
    /// <summary>The API handle of the product to subscribe to.</summary>
    public string ProductHandle { get; set; } = string.Empty;
    /// <summary>The ID of an existing Maxio customer.</summary>
    public int CustomerId { get; set; }
    /// <summary>The reference value (provided by your app) for the subscription itself.</summary>
    public string? Reference { get; set; }
    /// <summary>
    /// Collection method per the Collection-Method enum in the spec. "remittance"
    /// (invoice billing) allows signup without a stored payment method, matching
    /// the "Basic" request example for Create Subscription in the spec.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";
}

/// <summary>Plan view of a Maxio product, enriched for the subscription endpoints.</summary>
public class MaxioPlan
{
    public int ProductId { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public string? ProductFamilyHandle { get; set; }
}

/// <summary>Root error payload of Maxio error responses (errors/Error-List-Response).</summary>
public class MaxioErrorPayload
{
    public List<string>? Errors { get; set; }
}
using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

// Raw payloads of the Maxio Advanced Billing (Billing API) REST endpoints used by
// this integration. JSON uses snake_case, which is handled by the serializer
// options in MaxioBillingClient.

public sealed class MaxioProduct
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public DateTime? ArchivedAt { get; set; }
    public bool RequireCreditCard { get; set; }
    public MaxioProductFamilyRef? ProductFamily { get; set; }
}

public sealed class MaxioProductFamilyRef
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
}

public sealed class MaxioProductResponse
{
    public MaxioProduct? Product { get; set; }
}

public sealed class MaxioCustomer
{
    public int Id { get; set; }
    public string? Reference { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Organization { get; set; }
}

public sealed class MaxioCustomerResponse
{
    public MaxioCustomer? Customer { get; set; }
}

public sealed class MaxioCustomerInput
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
}

public sealed class MaxioSubscription
{
    public int Id { get; set; }
    public string? State { get; set; }
    public string? Reference { get; set; }
    public long ProductPriceInCents { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? CanceledAt { get; set; }
    public MaxioCustomer? Customer { get; set; }
    public MaxioProduct? Product { get; set; }
}

public sealed class MaxioSubscriptionResponse
{
    public MaxioSubscription? Subscription { get; set; }
}

public sealed class MaxioSubscriptionInput
{
    public string ProductHandle { get; set; } = string.Empty;
    public int CustomerId { get; set; }

    /// <summary>
    /// "invoice" collects payment manually (no card capture is attempted at
    /// signup), which lets shoppers subscribe to plans that do not require a
    /// payment method on file. Maxio maps this to the site's architecture
    /// (e.g. "remittance" on Relationship Invoicing sites).
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "invoice";
}

public sealed class MaxioCreateSubscriptionBody
{
    public MaxioSubscriptionInput Subscription { get; set; } = new();
}

public sealed class MaxioCreateCustomerBody
{
    public MaxioCustomerInput Customer { get; set; } = new();
}

/// <summary>
/// Thrown when the Maxio Billing API returns a non-success response.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(string message, int statusCode, string? responseBody)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public int StatusCode { get; }
    public string? ResponseBody { get; }
}

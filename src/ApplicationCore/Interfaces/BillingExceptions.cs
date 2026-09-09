using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Thrown when a shopper is not known to the billing system yet.
/// </summary>
public class SubscriptionPlanNotFoundException : Exception
{
    public string ProductHandle { get; }

    public SubscriptionPlanNotFoundException(string productHandle)
        : base($"Subscription plan '{productHandle}' was not found.")
    {
        ProductHandle = productHandle;
    }
}

/// <summary>
/// Thrown when the billing system (Maxio Advanced Billing) rejects a request.
/// </summary>
public class BillingException : Exception
{
    public int? StatusCode { get; }
    public string[] Errors { get; }

    public BillingException(string message, int? statusCode = null, string[]? errors = null)
        : base(message)
    {
        StatusCode = statusCode;
        Errors = errors ?? Array.Empty<string>();
    }
}

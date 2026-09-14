using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string productHandle)
        : base($"Subscription plan '{productHandle}' was not found in the configured product family.")
    {
    }
}

public sealed class SubscriptionProvisioningException : Exception
{
    public SubscriptionProvisioningException()
        : base("A subscription request for this plan is already being processed. Retry shortly.")
    {
    }
}

public sealed class BillingProviderException : Exception
{
    public BillingProviderException(string operation, int? statusCode, IReadOnlyList<string> errors, Exception? innerException = null)
        : base($"Maxio operation '{operation}' failed.", innerException)
    {
        Operation = operation;
        StatusCode = statusCode;
        Errors = errors;
    }

    public string Operation { get; }
    public int? StatusCode { get; }
    public IReadOnlyList<string> Errors { get; }
}

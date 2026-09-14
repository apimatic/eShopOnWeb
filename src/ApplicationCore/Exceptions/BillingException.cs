using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Base for failures raised by the subscription-billing integration. The message is safe to surface
/// to API callers: implementations must not put credentials or raw upstream payloads in it.
/// </summary>
public class BillingException : Exception
{
    public BillingException(string message) : base(message)
    {
    }

    public BillingException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>The requested plan does not exist in the configured product catalog.</summary>
public class BillingPlanNotFoundException : BillingException
{
    public BillingPlanNotFoundException(string message) : base(message)
    {
    }
}

/// <summary>
/// The billing system rejected the request as invalid (for example a plan that requires a payment
/// method, or a validation error on the customer record).
/// </summary>
public class BillingValidationException : BillingException
{
    public BillingValidationException(string message) : base(message)
    {
    }
}

/// <summary>
/// A concurrent or replayed request was detected upstream and the outcome could not be resolved to a
/// single subscription. The caller should re-read the subscription list before retrying.
/// </summary>
public class BillingConflictException : BillingException
{
    public BillingConflictException(string message) : base(message)
    {
    }
}

/// <summary>The billing system could not be reached, timed out, throttled us, or returned a server error.</summary>
public class BillingUnavailableException : BillingException
{
    public BillingUnavailableException(string message) : base(message)
    {
    }

    public BillingUnavailableException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>The billing integration is not configured (missing API key, subdomain or product family).</summary>
public class BillingConfigurationException : BillingException
{
    public BillingConfigurationException(string message) : base(message)
    {
    }
}

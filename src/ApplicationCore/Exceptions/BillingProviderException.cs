using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the billing provider rejects or fails a request. This is the single typed error
/// the provider seam surfaces, so no caller ever has to know about HTTP or the provider's wire
/// format.
/// </summary>
public class BillingProviderException : Exception
{
    private static readonly IReadOnlyCollection<string> NoErrors = Array.Empty<string>();

    public BillingProviderException(string message)
        : this(message, null, null, null)
    {
    }

    public BillingProviderException(string message, Exception? innerException)
        : this(message, null, null, innerException)
    {
    }

    public BillingProviderException(string message,
        int? statusCode,
        IEnumerable<string>? providerErrors,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ProviderErrors = providerErrors is null ? NoErrors : providerErrors.ToArray();
    }

    /// <summary>The provider's HTTP status code, when the failure reached the provider at all.</summary>
    public int? StatusCode { get; }

    /// <summary>Messages the provider returned, safe to surface to an operator.</summary>
    public IReadOnlyCollection<string> ProviderErrors { get; }
}

/// <summary>Raised when the billing provider rejects the credentials this integration presented.</summary>
public class BillingProviderAuthenticationException : BillingProviderException
{
    public BillingProviderAuthenticationException(string message, int? statusCode = null)
        : base(message, statusCode, null)
    {
    }
}

/// <summary>Raised when the requested entity does not exist at the billing provider.</summary>
public class BillingProviderNotFoundException : BillingProviderException
{
    public BillingProviderNotFoundException(string message, int? statusCode = null)
        : base(message, statusCode, null)
    {
    }
}

/// <summary>
/// Raised when the billing provider refuses a request as invalid — for example an illegal
/// lifecycle transition, or a plan change to the plan the subscription is already on.
/// </summary>
public class BillingProviderValidationException : BillingProviderException
{
    public BillingProviderValidationException(string message,
        int? statusCode = null,
        IEnumerable<string>? providerErrors = null)
        : base(message, statusCode, providerErrors)
    {
    }
}

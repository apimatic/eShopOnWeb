using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>Base type for every failure raised by the billing integration.</summary>
public abstract class BillingException : Exception
{
    protected BillingException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// The billing integration is not usable because required configuration is missing or invalid.
/// Surfaced as a server-side fault: shoppers cannot fix it and retrying will not help.
/// </summary>
public class BillingConfigurationException : BillingException
{
    public BillingConfigurationException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>The requested plan handle does not exist in the configured catalog.</summary>
public class PlanNotFoundException : BillingException
{
    public PlanNotFoundException(string planHandle, string? productFamilyHandle)
        : base($"Plan '{planHandle}' was not found" +
               (productFamilyHandle is null ? "." : $" in product family '{productFamilyHandle}'."))
    {
        PlanHandle = planHandle;
        ProductFamilyHandle = productFamilyHandle;
    }

    public string PlanHandle { get; }

    public string? ProductFamilyHandle { get; }
}

/// <summary>
/// The billing provider rejected the request as invalid (for example a plan that requires a
/// payment method the shopper has not supplied). The caller must change the request to succeed.
/// </summary>
public class BillingValidationException : BillingException
{
    public BillingValidationException(string message, IReadOnlyList<string>? errors = null)
        : base(message)
    {
        Errors = errors ?? Array.Empty<string>();
    }

    /// <summary>Provider-reported validation messages, safe to relay to the caller.</summary>
    public IReadOnlyList<string> Errors { get; }
}

/// <summary>
/// The billing provider was reachable but failed the request, or could not be reached at all.
/// Represents an upstream fault rather than a caller mistake.
/// </summary>
public class BillingProviderException : BillingException
{
    public BillingProviderException(string message, int? statusCode = null,
        IReadOnlyList<string>? errors = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Errors = errors ?? Array.Empty<string>();
    }

    /// <summary>HTTP status returned by the provider, when the call completed.</summary>
    public int? StatusCode { get; }

    public IReadOnlyList<string> Errors { get; }

    public override string ToString() => Errors.Any()
        ? $"{base.ToString()}{Environment.NewLine}Provider errors: {string.Join("; ", Errors)}"
        : base.ToString();
}

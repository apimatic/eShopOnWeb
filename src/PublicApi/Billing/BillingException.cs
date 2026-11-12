using System;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>A request the app cannot carry out; carries the HTTP status and a caller-safe reason.</summary>
public class BillingException : Exception
{
    public int StatusCode { get; }

    /// <summary>True when the provider may have acted although no usable answer arrived (a write's outcome is unknown).</summary>
    public bool OutcomeUnknown { get; init; }

    public BillingException(int statusCode, string message, Exception? inner = null) : base(message, inner)
    {
        StatusCode = statusCode;
    }
}

using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// The one failure type the subscription endpoints surface. It carries the HTTP status the caller should see
/// and a caller-safe message — never an SDK exception message, URL or type name.
/// </summary>
public class BillingException : Exception
{
    public BillingException(int statusCode, string message, IReadOnlyList<string>? errors = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Errors = errors ?? Array.Empty<string>();
    }

    public int StatusCode { get; }

    /// <summary>Provider validation messages that are safe and useful to show the caller.</summary>
    public IReadOnlyList<string> Errors { get; }
}

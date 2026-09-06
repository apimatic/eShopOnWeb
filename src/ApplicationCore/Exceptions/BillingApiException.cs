using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The billing system rejected or failed a request. <see cref="StatusCode"/> is the upstream
/// HTTP status (0 when the call never produced one) and <see cref="Errors"/> the messages it returned.
/// </summary>
public class BillingApiException : Exception
{
    public BillingApiException(string message, int statusCode, IReadOnlyList<string>? errors = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Errors = errors ?? Array.Empty<string>();
    }

    public int StatusCode { get; }

    public IReadOnlyList<string> Errors { get; }

    /// <summary>
    /// True when the caller's input caused the failure (validation / not found / conflict),
    /// as opposed to an upstream outage.
    /// </summary>
    public bool IsCallerFault => StatusCode is >= 400 and < 500 and not 429;

    public override string ToString() => Errors.Any()
        ? $"{base.ToString()}{Environment.NewLine}Billing errors: {string.Join("; ", Errors)}"
        : base.ToString();
}

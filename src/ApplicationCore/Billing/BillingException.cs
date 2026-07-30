using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// Raised when a billing operation cannot be completed. <see cref="StatusCode"/> carries
/// the HTTP status the API surface should return to the caller (404 for an unknown plan,
/// 400 for a rejected request, 502 for an upstream Maxio failure), keeping the endpoint
/// mapping trivial. <see cref="Errors"/> holds any messages Maxio returned verbatim.
/// </summary>
public class BillingException : Exception
{
    public BillingException(string message, int statusCode = 502, IReadOnlyList<string>? errors = null)
        : base(message)
    {
        StatusCode = statusCode;
        Errors = errors ?? Array.Empty<string>();
    }

    public int StatusCode { get; }
    public IReadOnlyList<string> Errors { get; }
}

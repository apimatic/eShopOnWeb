using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// A failed call to the Maxio API. Deliberately scoped to the Infrastructure layer: callers outside
/// it see the provider-agnostic <see cref="ApplicationCore.Exceptions.BillingProviderException"/>.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(
        string message,
        HttpStatusCode? statusCode,
        IEnumerable<string>? errors = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Errors = errors?.ToArray() ?? Array.Empty<string>();
    }

    public HttpStatusCode? StatusCode { get; }

    public IReadOnlyList<string> Errors { get; }

    /// <summary>
    /// True when Maxio rejected the write because the <c>reference</c> we supplied is already taken.
    /// This is the signal that a concurrent or replayed request already created the record, and is
    /// how create-or-adopt stays correct without a local mapping table.
    /// </summary>
    public bool IsReferenceAlreadyTaken =>
        StatusCode == HttpStatusCode.UnprocessableEntity &&
        Errors.Any(e =>
            e.Contains("reference", StringComparison.OrdinalIgnoreCase) &&
            e.Contains("unique", StringComparison.OrdinalIgnoreCase));

    /// <summary>True when Maxio blamed the request rather than itself.</summary>
    public bool IsCallerError => StatusCode is { } code && (int)code >= 400 && (int)code < 500;
}

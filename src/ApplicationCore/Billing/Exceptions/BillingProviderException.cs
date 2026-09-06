using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Billing.Exceptions;

/// <summary>
/// The billing provider rejected the call or was unreachable. <see cref="StatusCode"/> carries
/// the upstream HTTP status when there was one, and <see cref="Errors"/> the messages parsed out
/// of the error models the provider specification defines.
/// </summary>
public class BillingProviderException : BillingException
{
    private static readonly IReadOnlyList<string> NoErrors = Array.Empty<string>();

    public BillingProviderException(
        string message,
        int? statusCode = null,
        IReadOnlyList<string>? errors = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Errors = errors ?? NoErrors;
    }

    /// <summary>Upstream HTTP status code, when the failure was an HTTP response.</summary>
    public int? StatusCode { get; }

    /// <summary>Human-readable messages extracted from the error payload of the provider.</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>
    /// True when the upstream status indicates the request itself was invalid (the caller can fix
    /// it) rather than an upstream outage.
    /// </summary>
    public bool IsUpstreamValidationFailure => StatusCode is >= 400 and < 500 and not 401 and not 403 and not 429;
}

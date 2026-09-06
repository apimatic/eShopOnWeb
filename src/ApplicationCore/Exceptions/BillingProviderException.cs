using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Something went wrong while talking to the external billing system of record.
/// <see cref="IsCallerFault"/> separates "the request we were asked to make is not valid" from
/// "the provider is unavailable or rejected us", so the API can answer 4xx or 5xx accordingly.
/// </summary>
public class BillingProviderException : Exception
{
    public BillingProviderException(
        string message,
        int? statusCode = null,
        IReadOnlyCollection<string>? providerErrors = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ProviderErrors = providerErrors ?? Array.Empty<string>();
    }

    /// <summary>HTTP status returned by the provider, when the call reached it.</summary>
    public int? StatusCode { get; }

    /// <summary>Error messages reported by the provider, verbatim.</summary>
    public IReadOnlyCollection<string> ProviderErrors { get; }

    /// <summary>True when the provider rejected the content of the request rather than failing.</summary>
    public virtual bool IsCallerFault =>
        StatusCode is >= 400 and < 500 and not 401 and not 403 and not 429;

    /// <summary>True when the provider throttled us and the call can be retried later.</summary>
    public virtual bool IsThrottled => StatusCode == 429;

    public string ProviderErrorSummary => ProviderErrors.Any()
        ? string.Join("; ", ProviderErrors)
        : Message;
}

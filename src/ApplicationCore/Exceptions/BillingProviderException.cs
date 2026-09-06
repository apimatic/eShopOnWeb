using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The billing provider could not serve a request. <see cref="IsClientError"/> distinguishes a
/// request this application got wrong (surface as 4xx) from a provider-side or transport failure
/// (surface as 502/503).
/// </summary>
public class BillingProviderException : Exception
{
    public BillingProviderException(string message, bool isClientError = false, IReadOnlyList<string>? errors = null, Exception? innerException = null)
        : base(message, innerException)
    {
        IsClientError = isClientError;
        Errors = errors ?? Array.Empty<string>();
    }

    /// <summary>True when the provider rejected the request as invalid rather than failing to serve it.</summary>
    public bool IsClientError { get; }

    /// <summary>Provider-supplied error messages, safe to echo back to the caller.</summary>
    public IReadOnlyList<string> Errors { get; }

    public override string ToString() =>
        Errors.Any() ? $"{base.ToString()}{Environment.NewLine}Provider errors: {string.Join("; ", Errors)}" : base.ToString();
}

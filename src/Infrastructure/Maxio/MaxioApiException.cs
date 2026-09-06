using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// A non-success response from the Maxio API, carrying the provider's own error messages.
/// The specification models errors as <c>{ "errors": [...] }</c>, <c>{ "errors": { "customer": "..." } }</c>
/// or <c>{ "errors": "..." }</c> depending on the operation; all three shapes are flattened into
/// <see cref="BillingProviderException.ProviderErrors"/>.
/// </summary>
public class MaxioApiException : BillingProviderException
{
    private const string DuplicateReferenceMarker = "must be unique";

    public MaxioApiException(
        string message,
        int? statusCode = null,
        IReadOnlyCollection<string>? providerErrors = null,
        Exception? innerException = null)
        : base(message, statusCode, providerErrors, innerException)
    {
    }

    /// <summary>
    /// True when Maxio rejected the write because the reference we supplied is already taken.
    /// That is the signal a concurrent or replayed request already created the record, so the caller
    /// should read the existing one instead of failing.
    /// </summary>
    public bool IsDuplicateReference =>
        StatusCode == 422 &&
        ProviderErrors.Any(e => e.Contains(DuplicateReferenceMarker, StringComparison.OrdinalIgnoreCase));
}

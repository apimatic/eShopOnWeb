using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when an interaction with the billing system of record (Maxio Advanced Billing) fails.
/// <see cref="IsClientError"/> distinguishes caller-fixable problems (e.g. an invalid plan handle,
/// a validation error) from server/transient/configuration failures, so the API surface can map
/// them to the right HTTP status code.
/// </summary>
public class BillingException : Exception
{
    public BillingException(string message, bool isClientError = false, Exception? innerException = null)
        : base(message, innerException)
    {
        IsClientError = isClientError;
    }

    /// <summary>True when the failure was caused by the request itself (4xx-style), false for server/transient failures.</summary>
    public bool IsClientError { get; }
}

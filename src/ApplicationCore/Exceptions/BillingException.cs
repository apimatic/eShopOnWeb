using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when an operation against the billing system of record cannot be completed,
/// e.g. an unknown plan or a rejected request. Carries the HTTP status the API layer
/// should surface to the caller.
/// </summary>
public class BillingException : Exception
{
    public int? StatusCode { get; }

    public BillingException(string message, int? statusCode = null) : base(message)
    {
        StatusCode = statusCode;
    }
}
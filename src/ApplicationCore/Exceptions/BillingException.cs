using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Base type for failures raised by the subscription-billing integration. The API layer maps these
/// onto HTTP status codes, so every billing failure surfaces as something better than a bare 500.
/// </summary>
public abstract class BillingException : Exception
{
    protected BillingException(string message) : base(message)
    {
    }

    protected BillingException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

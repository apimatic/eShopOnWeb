using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The billing provider rejected the request as invalid (HTTP 422). The caller can act on this, so
/// it is surfaced as 422 with the provider's validation messages (already sanitised to a
/// caller-safe string), rather than a 5xx.
/// </summary>
public sealed class BillingValidationException : BillingException
{
    public BillingValidationException(string message, Exception? innerException = null)
        : base(message, (int)HttpStatusCode.UnprocessableEntity, innerException)
    {
    }
}

using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when PayPal requires a shopper to complete a browser challenge (e.g. 3-D Secure).
/// The task forbids building an approval round-trip; this is reported as a contract/runtime gap.
/// </summary>
public class PayerActionRequiredException : Exception
{
    public PayerActionRequiredException(string message) : base(message)
    {
    }
}

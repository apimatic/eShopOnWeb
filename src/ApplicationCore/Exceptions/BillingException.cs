using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A failure at the billing-provider boundary. <see cref="StatusCode"/> is the HTTP status the
/// API should answer with: provider 4xx are carried through (the caller can act on them);
/// transport failures and unreadable provider responses surface as 5xx.
/// The message is always caller-safe — never raw SDK/framework exception text.
/// </summary>
public class BillingException : Exception
{
    public BillingException(int statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}

using System;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// The single failure type leaving the billing integration boundary. Carries the HTTP
/// status the caller should see (provider 4xx are carried through; transport failures and
/// unreadable provider responses surface as 502) and a caller-safe message.
/// </summary>
public class MaxioBillingException : Exception
{
    public int StatusCode { get; }

    public MaxioBillingException(int statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}

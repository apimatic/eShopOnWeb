using System;

namespace Microsoft.eShopWeb.Infrastructure.Sms;

/// <summary>
/// Raised when a messaging-provider call fails for a reason the caller cannot recover from within
/// the request (auth, transport, or a provider error on an operation that must succeed). Its message
/// never contains the auth token.
/// </summary>
public class SmsProviderException : Exception
{
    public SmsProviderException(string message) : base(message)
    {
    }

    public SmsProviderException(string message, Exception inner) : base(message, inner)
    {
    }
}

using System;

namespace Microsoft.eShopWeb.Infrastructure.Sms;

/// <summary>
/// Raised when a call to the messaging provider fails in a way the caller must know about
/// (e.g. a lookup, a status read, a cancel or a redaction could not be completed). Order
/// notifications treat sends as best-effort and swallow this; operator actions surface it.
/// The message never contains the auth token or a shopper's phone number.
/// </summary>
public class SmsProviderException : Exception
{
    public SmsProviderException(string message) : base(message)
    {
    }
}

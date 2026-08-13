using System;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Raised for unexpected provider or transport failures (5xx, network) where no delivery outcome could be
/// obtained. Documented request-level rejections are returned as results, not thrown. The message never
/// includes a shopper's phone number.
/// </summary>
public class SmsProviderException : Exception
{
    public SmsProviderException(string message) : base(message) { }
    public SmsProviderException(string message, Exception inner) : base(message, inner) { }
}

using System;

namespace Microsoft.eShopWeb.ApplicationCore.Sms;

/// <summary>
/// Raised when the SMS provider rejects or fails a request. Its message is deliberately kept free of
/// any destination number so it is safe to log.
/// </summary>
public class SmsGatewayException : Exception
{
    public SmsGatewayException(string message) : base(message) { }
    public SmsGatewayException(string message, Exception inner) : base(message, inner) { }
}

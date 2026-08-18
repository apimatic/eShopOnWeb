using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Raised when the messaging provider refuses a request (non-success response or transport
/// failure). Orchestration treats these as best-effort outcomes — they must never fail the
/// underlying order operation.
/// </summary>
public class SmsGatewayException : Exception
{
    public SmsGatewayException(string message) : base(message) { }

    public SmsGatewayException(string message, Exception innerException) : base(message, innerException) { }
}

using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A non-success response from the Twilio API. Twilio error responses carry a JSON body
/// with code/message/status (see the error model in the Twilio OpenAPI specification).
/// </summary>
public class TwilioApiException : Exception
{
    public int StatusCode { get; }
    public int? ErrorCode { get; }

    public TwilioApiException(int statusCode, int? errorCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }
}

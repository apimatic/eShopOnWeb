using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The provider rejected a request. Carries the provider's own error model
/// (HTTP status, Twilio error code and message).
/// </summary>
public class TwilioApiException : Exception
{
    public TwilioApiException(int httpStatusCode, int? errorCode, string? errorMessage)
        : base($"Twilio request failed (HTTP {httpStatusCode}, error {errorCode}): {errorMessage}")
    {
        HttpStatusCode = httpStatusCode;
        ErrorCode = errorCode;
        TwilioErrorMessage = errorMessage;
    }

    public int HttpStatusCode { get; }
    public int? ErrorCode { get; }
    public string? TwilioErrorMessage { get; }
}

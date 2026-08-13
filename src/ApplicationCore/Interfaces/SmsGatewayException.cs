using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Raised when the SMS provider could not fulfil a request the caller is entitled to have answered
/// (validation, status read, reconciliation, redaction). Carries a caller-safe message and, where the
/// provider supplied one, the HTTP status it rejected with — never any provider secret or shopper number.
/// Sending operations never raise this: a send failure is reported as an unsuccessful
/// <see cref="SmsDispatchResult"/> instead, so it can never fail the underlying order operation.
/// </summary>
public class SmsGatewayException : Exception
{
    public SmsGatewayException(string message) : base(message) { }

    public SmsGatewayException(string message, Exception innerException) : base(message, innerException) { }

    /// <summary>The provider's HTTP status, when the failure was an error response rather than a transport fault.</summary>
    public int? ProviderStatusCode { get; init; }
}

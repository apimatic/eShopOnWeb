using System;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// A payment failure that can be returned to the caller with a stable HTTP status and a
/// machine-readable code. Provider messages are sanitised before they reach this type.
/// </summary>
public class PaymentException : Exception
{
    public PaymentException(
        int statusCode,
        string code,
        string message,
        Exception? innerException = null,
        bool unknownOutcome = false,
        string? providerLogref = null,
        int? providerStatusCode = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Code = code;
        UnknownOutcome = unknownOutcome;
        ProviderLogref = providerLogref;
        ProviderStatusCode = providerStatusCode;
    }

    public int StatusCode { get; }
    public string Code { get; }
    public bool UnknownOutcome { get; }
    public string? ProviderLogref { get; }
    public int? ProviderStatusCode { get; }

    public static PaymentException BadRequest(string code, string message) => new(400, code, message);
    public static PaymentException NotFound(string code, string message) => new(404, code, message);
    public static PaymentException Conflict(string code, string message) => new(409, code, message);
}

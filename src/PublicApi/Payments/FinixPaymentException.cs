using System;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// A caller-facing payment failure. <see cref="StatusCode"/> is the HTTP status the
/// endpoint returns; <see cref="Code"/> is a stable machine-readable reason.
/// </summary>
public class FinixPaymentException : Exception
{
    public FinixPaymentException(int statusCode, string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public int StatusCode { get; }
    public string Code { get; }
    public int? ProviderStatusCode { get; init; }
    public string? LogRef { get; init; }
}

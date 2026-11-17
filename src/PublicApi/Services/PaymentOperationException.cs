using System;

namespace Microsoft.eShopWeb.PublicApi.Services;

public enum PaymentFailureKind
{
    Validation,
    NotFound,
    Conflict,
    Upstream
}

/// <summary>
/// A payment operation failure with enough information for an endpoint to return a useful
/// HTTP response. Messages never contain card data.
/// </summary>
public sealed class PaymentOperationException : Exception
{
    public PaymentOperationException(
        string message,
        PaymentFailureKind kind = PaymentFailureKind.Conflict,
        string? finixCode = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        FinixCode = finixCode;
    }

    public PaymentFailureKind Kind { get; }

    public string? FinixCode { get; }
}

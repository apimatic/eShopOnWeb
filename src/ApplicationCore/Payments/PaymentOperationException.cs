using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public enum PaymentErrorKind { InvalidRequest, NotFound, Conflict, PayPalUnavailable, ShopperActionRequired }

public sealed class PaymentOperationException : Exception
{
    public PaymentOperationException(PaymentErrorKind kind, string message, string? debugId = null,
        Exception? innerException = null) : base(message, innerException)
    {
        Kind = kind;
        DebugId = debugId;
    }

    public PaymentErrorKind Kind { get; }
    public string? DebugId { get; }
}

using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public enum PaymentErrorKind
{
    Validation,
    NotFound,
    Conflict,
    ProcessorRejected,
    ProcessorUnavailable,
    PayerActionRequired
}

public sealed class PaymentOperationException : Exception
{
    public PaymentOperationException(PaymentErrorKind kind, string code, string message,
        Exception? innerException = null) : base(message, innerException)
    {
        Kind = kind;
        Code = code;
    }

    public PaymentErrorKind Kind { get; }
    public string Code { get; }
}

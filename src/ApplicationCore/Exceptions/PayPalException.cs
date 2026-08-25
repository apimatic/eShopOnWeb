using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public enum PayPalErrorKind
{
    General,
    PayerActionRequired,
    ReauthorizationImpossible,
    Conflict,
    NotFound
}

public class PayPalException : Exception
{
    public PayPalErrorKind Kind { get; }

    public PayPalException(string message, PayPalErrorKind kind = PayPalErrorKind.General, Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
    }
}

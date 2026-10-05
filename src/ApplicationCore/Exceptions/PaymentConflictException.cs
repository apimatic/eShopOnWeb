using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>The request is valid but conflicts with the current state of the order or payment (HTTP 409).</summary>
public class PaymentConflictException : Exception
{
    public PaymentConflictException(string message, string? code = null) : base(message)
    {
        Code = code;
    }

    public string? Code { get; }
}

using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>The caller's request is invalid (HTTP 400).</summary>
public class PaymentValidationException : Exception
{
    public PaymentValidationException(string message) : base(message)
    {
    }
}

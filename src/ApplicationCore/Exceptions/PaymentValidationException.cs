using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PaymentValidationException : Exception
{
    public PaymentValidationException(string message) : base(message)
    {
    }
}

using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PaymentStateException : Exception
{
    public PaymentStateException(string message) : base(message)
    {
    }
}

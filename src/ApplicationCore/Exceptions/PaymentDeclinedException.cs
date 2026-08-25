using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PaymentDeclinedException : Exception
{
    public PaymentDeclinedException(string message) : base(message)
    {
    }
}

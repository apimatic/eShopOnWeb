using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PaymentActionRequiredException : Exception
{
    public PaymentActionRequiredException(string message) : base(message)
    {
    }
}

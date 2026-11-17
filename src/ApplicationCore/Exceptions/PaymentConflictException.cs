using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PaymentConflictException : Exception
{
    public PaymentConflictException(string message) : base(message) { }
}

using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class RefundExceedsCapturedAmountException : Exception
{
    public RefundExceedsCapturedAmountException(string message) : base(message)
    {
    }
}

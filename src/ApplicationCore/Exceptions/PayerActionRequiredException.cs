namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PayerActionRequiredException : PaymentException
{
    public PayerActionRequiredException(string message)
        : base(409, message)
    {
    }
}

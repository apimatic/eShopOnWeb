namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PayerActionRequiredException : OrderPaymentException
{
    public PayerActionRequiredException(string message)
        : base(409, message)
    {
    }
}

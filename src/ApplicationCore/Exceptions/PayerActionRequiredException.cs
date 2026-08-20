namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PayerActionRequiredException : PaymentException
{
    public PayerActionRequiredException(string message)
        : base(message, statusCode: 409, errorCode: "PAYER_ACTION_REQUIRED")
    {
    }
}

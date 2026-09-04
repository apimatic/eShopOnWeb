namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PaymentMethodNotFoundException : ApiException
{
    public PaymentMethodNotFoundException()
        : base("The requested payment method was not found.", 404)
    {
    }
}
namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class BillingValidationException : BillingException
{
    public BillingValidationException(string message) : base(message)
    {
    }
}

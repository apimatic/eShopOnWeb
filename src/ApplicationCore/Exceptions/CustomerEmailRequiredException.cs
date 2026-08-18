namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class CustomerEmailRequiredException : BillingException
{
    public CustomerEmailRequiredException()
        : base(400, "A verified email address is required before you can subscribe.")
    {
    }
}

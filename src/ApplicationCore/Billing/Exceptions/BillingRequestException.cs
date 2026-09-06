namespace Microsoft.eShopWeb.ApplicationCore.Billing.Exceptions;

/// <summary>
/// The billing request itself is invalid &#8212; something the caller can correct. Maps to a 400.
/// </summary>
public class BillingRequestException : BillingException
{
    public BillingRequestException(string message) : base(message)
    {
    }
}

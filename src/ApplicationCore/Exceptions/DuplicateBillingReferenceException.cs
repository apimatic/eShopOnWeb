namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A customer with the requested reference already exists. Expected when two concurrent
/// requests both decide the shopper needs a billing customer; the loser re-reads it.
/// </summary>
public class DuplicateBillingReferenceException : BillingGatewayException
{
    public DuplicateBillingReferenceException(string reference)
        : base($"A billing customer already exists for reference '{reference}'.")
    {
        Reference = reference;
    }

    public string Reference { get; }
}

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The billing system refused the request because it collides with an existing record: either a
/// duplicate-prevention token that was already used, or a customer reference that is already taken.
/// </summary>
public class BillingConflictException : BillingException
{
    public BillingConflictException(string message) : base(message)
    {
    }
}

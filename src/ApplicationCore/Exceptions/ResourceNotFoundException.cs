using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Something the caller named does not exist for them. Used for both "not there" and "not yours", so
/// one shopper can never tell whether another shopper's order or card exists.
/// </summary>
public class ResourceNotFoundException : Exception
{
    public ResourceNotFoundException(string message) : base(message)
    {
    }
}

/// <summary>
/// A hold that can no longer be renewed. The message is written for an operator and says what to do.
/// </summary>
public class PaymentRenewalFailedException : Exception
{
    public PaymentRenewalFailedException(string message) : base(message)
    {
    }
}

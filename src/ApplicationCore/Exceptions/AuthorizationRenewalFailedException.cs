using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class AuthorizationRenewalFailedException : Exception
{
    public AuthorizationRenewalFailedException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}

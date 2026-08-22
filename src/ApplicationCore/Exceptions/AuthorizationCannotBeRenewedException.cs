using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class AuthorizationCannotBeRenewedException : Exception
{
    public AuthorizationCannotBeRenewedException(string message) : base(message)
    {
    }
}

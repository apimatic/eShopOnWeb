using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class ContactNumberRejectedException : Exception
{
    public ContactNumberRejectedException(string message) : base(message)
    {
    }
}

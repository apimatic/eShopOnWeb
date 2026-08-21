using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class ContactNumberNotUsableException : Exception
{
    public ContactNumberNotUsableException(string message) : base(message)
    {
    }
}

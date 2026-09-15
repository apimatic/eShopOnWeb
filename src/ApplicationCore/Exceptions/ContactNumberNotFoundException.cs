using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class ContactNumberNotFoundException : Exception
{
    public ContactNumberNotFoundException() : base("The contact number was not found.")
    {
    }
}

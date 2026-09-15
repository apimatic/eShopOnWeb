using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class ContactNumberNotFoundException : Exception
{
    public ContactNumberNotFoundException(int contactNumberId)
        : base($"Contact number {contactNumberId} was not found.")
    {
    }
}

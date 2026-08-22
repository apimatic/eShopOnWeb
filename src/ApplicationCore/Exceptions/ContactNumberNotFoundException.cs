using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class ContactNumberNotFoundException : Exception
{
    public ContactNumberNotFoundException(int contactNumberId)
        : base($"No contact number found with id {contactNumberId}")
    {
    }
}

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class ContactNumberNotFoundException : System.Exception
{
    public ContactNumberNotFoundException(int contactNumberId)
        : base($"Contact number {contactNumberId} was not found.")
    {
    }
}

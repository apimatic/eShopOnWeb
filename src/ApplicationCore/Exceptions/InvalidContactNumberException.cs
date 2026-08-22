namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class InvalidContactNumberException : BadRequestException
{
    public InvalidContactNumberException()
        : base("The phone number is not a usable destination.")
    {
    }
}

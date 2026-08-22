namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class ConflictException : DuplicateException
{
    public ConflictException(string message) : base(message)
    {
    }
}

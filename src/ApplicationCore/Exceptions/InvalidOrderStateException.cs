namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class InvalidOrderStateException : ApiException
{
    public InvalidOrderStateException(string message, int statusCode = 409)
        : base(message, statusCode)
    {
    }
}
namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class InvalidOrderTransitionException : BadRequestException
{
    public InvalidOrderTransitionException(string message) : base(message)
    {
    }
}

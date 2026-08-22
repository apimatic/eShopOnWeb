namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class NotificationActionException : BadRequestException
{
    public NotificationActionException(string message) : base(message)
    {
    }
}

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class AuthorizationUnrenewableException : PaymentException
{
    public AuthorizationUnrenewableException(string message)
        : base(message, statusCode: 409, errorCode: "AUTHORIZATION_UNRENEWABLE")
    {
    }
}

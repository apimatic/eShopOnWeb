using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PaymentException : Exception
{
    public PaymentException(string message) : base(message) { }

    public PaymentException(string message, Exception innerException) : base(message, innerException) { }
}

public class PaymentValidationException : PaymentException
{
    public PaymentValidationException(string message) : base(message) { }
}

public class PaymentConflictException : PaymentException
{
    public PaymentConflictException(string message) : base(message) { }
}

public class PaymentForbiddenException : PaymentException
{
    public PaymentForbiddenException(string message) : base(message) { }
}

public class PaymentNotFoundException : PaymentException
{
    public PaymentNotFoundException(string message) : base(message) { }
}

public class AuthorizationCannotBeRenewedException : PaymentException
{
    public AuthorizationCannotBeRenewedException(string message) : base(message) { }
}

public class PayerActionRequiredException : PaymentException
{
    public PayerActionRequiredException(string message) : base(message) { }
}

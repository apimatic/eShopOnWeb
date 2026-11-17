using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) {}
}

public class PaymentValidationException : Exception
{
    public PaymentValidationException(string message) : base(message) {}
}

public class PaymentConflictException : Exception
{
    public PaymentConflictException(string message) : base(message) {}
}

public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message, Exception? innerException = null)
        : base(message, innerException) {}
}

using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PaymentValidationException : Exception
{
    public PaymentValidationException(string message) : base(message) { }
}

public class PaymentNotFoundException : Exception
{
    public PaymentNotFoundException(string message) : base(message) { }
}

public class PaymentConflictException : Exception
{
    public PaymentConflictException(string message) : base(message) { }
}

public class PaymentForbiddenException : Exception
{
    public PaymentForbiddenException(string message) : base(message) { }
}

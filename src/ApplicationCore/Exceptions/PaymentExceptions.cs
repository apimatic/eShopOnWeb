using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PaymentValidationException : Exception
{
    public PaymentValidationException(string message) : base(message) { }
}

public class PaymentResourceNotFoundException : Exception
{
    public PaymentResourceNotFoundException(string message) : base(message) { }
}

public class PaymentConflictException : Exception
{
    public PaymentConflictException(string message) : base(message) { }
}

public class PaymentProcessorException : Exception
{
    public PaymentProcessorException(string message) : base(message) { }
}

public class PayerActionRequiredException : PaymentConflictException
{
    public PayerActionRequiredException()
        : base("PayPal requires browser-based payer authentication for this card. This API does not support an approval round-trip; use a card that does not require a challenge.")
    {
    }
}

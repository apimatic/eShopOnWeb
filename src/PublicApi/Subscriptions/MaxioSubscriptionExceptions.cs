using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public class MaxioSubscriptionException : Exception
{
    public MaxioSubscriptionException(int statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}

public sealed class MaxioSubscriptionNotFoundException : MaxioSubscriptionException
{
    public MaxioSubscriptionNotFoundException(string message)
        : base(404, message) { }
}

public sealed class MaxioWriteAlreadySentException : Exception
{
    public MaxioWriteAlreadySentException()
        : base("The provider write was already attempted.") { }
}

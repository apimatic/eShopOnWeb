using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>Maxio refused the request (a business/validation rejection the caller can act on).</summary>
public class MaxioRejectedException : Exception
{
    public MaxioRejectedException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>Maxio could not be used: transport failure, our credentials/quota, 5xx, unreadable error.</summary>
public class MaxioUnavailableException : Exception
{
    public HttpStatusCode? Upstream { get; }
    public MaxioUnavailableException(string message, HttpStatusCode? upstream, Exception? inner = null) : base(message, inner)
        => Upstream = upstream;
}

/// <summary>A write may or may not have landed at Maxio; the caller must reconcile by reference.</summary>
public class MaxioOutcomeUnknownException : Exception
{
    public MaxioOutcomeUnknownException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>Application-level failure surfaced to the HTTP layer with a safe message.</summary>
public class SubscriptionException : Exception
{
    public int StatusCode { get; }
    public SubscriptionException(int statusCode, string message, Exception? inner = null) : base(message, inner)
        => StatusCode = statusCode;
}

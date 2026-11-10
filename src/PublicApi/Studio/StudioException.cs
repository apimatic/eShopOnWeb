using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>
/// A request the studio integration cannot carry out; the middleware answers with <see cref="StatusCode"/> and the message.
/// </summary>
public class StudioException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public StudioException(HttpStatusCode statusCode, string message, Exception? inner = null) : base(message, inner)
    {
        StatusCode = statusCode;
    }

    public static StudioException BadRequest(string message) => new(HttpStatusCode.BadRequest, message);
    public static StudioException NotFound(string message) => new(HttpStatusCode.NotFound, message);
    public static StudioException Conflict(string message) => new(HttpStatusCode.Conflict, message);
}

/// <summary>
/// Mindbody answered a write with success but the SDK could not read the reply: the change was applied, its details are unknown.
/// </summary>
public sealed class UnreadableReplyException : StudioException
{
    public UnreadableReplyException(Exception inner)
        : base(HttpStatusCode.BadGateway, "The studio system carried out the request but its reply could not be read.", inner)
    {
    }
}

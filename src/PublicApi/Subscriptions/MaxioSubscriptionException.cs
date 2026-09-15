using System;
using System.Net;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioSubscriptionException : Exception
{
    public MaxioSubscriptionException(string message, HttpStatusCode? providerStatusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderStatusCode = providerStatusCode;
    }

    public HttpStatusCode? ProviderStatusCode { get; }

    public int ClientStatusCode => ProviderStatusCode is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError
        ? (int)ProviderStatusCode.Value
        : StatusCodes.Status502BadGateway;
}

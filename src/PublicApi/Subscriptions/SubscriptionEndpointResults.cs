using System;
using System.Net;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

internal static class SubscriptionEndpointResults
{
    public static IResult From(Exception exception) => exception switch
    {
        SubscriptionRequestException request => Results.Problem(request.Message, statusCode: StatusCodes.Status400BadRequest),
        MaxioProviderException { ProviderStatus: >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError } provider =>
            Results.Problem(provider.Message, statusCode: (int)provider.ProviderStatus!.Value),
        MaxioProviderException provider => Results.Problem(provider.Message, statusCode: StatusCodes.Status502BadGateway),
        _ => Results.Problem("The subscription request could not be completed.", statusCode: StatusCodes.Status500InternalServerError)
    };
}

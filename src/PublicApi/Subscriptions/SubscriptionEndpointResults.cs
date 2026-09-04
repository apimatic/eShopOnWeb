using System.Net;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

internal static class SubscriptionEndpointResults
{
    public static IResult FromException(System.Exception exception) => exception switch
    {
        SubscriptionValidationException validation => Results.BadRequest(new { error = validation.Message }),
        SubscriptionPlanNotFoundException notFound => Results.NotFound(new { error = notFound.Message }),
        MaxioConfigurationException configuration => Results.Problem(configuration.Message, statusCode: StatusCodes.Status503ServiceUnavailable),
        MaxioApiException => Results.Problem("The billing provider could not complete the request.", statusCode: StatusCodes.Status502BadGateway),
        _ => Results.Problem("The subscription request could not be completed.", statusCode: StatusCodes.Status500InternalServerError)
    };
}

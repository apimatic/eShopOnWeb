using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.Infrastructure.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Turns Maxio failures into HTTP problem responses for the subscription endpoints.
/// </summary>
internal static class SubscriptionEndpointErrors
{
    public static IResult FromMaxioApi(MaxioApiException ex)
    {
        var detail = ex.Errors.Count > 0 ? string.Join(" ", ex.Errors) : ex.Message;
        return Results.Problem(
            title: "The Maxio billing request failed.",
            detail: detail,
            statusCode: ex.StatusCode >= System.Net.HttpStatusCode.BadRequest && ex.StatusCode < System.Net.HttpStatusCode.InternalServerError
                ? (int)ex.StatusCode
                : StatusCodes.Status502BadGateway);
    }

    public static IResult FromConfiguration(MaxioConfigurationException ex)
    {
        return Results.Problem(
            title: "Maxio billing is not configured.",
            detail: ex.Message,
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}

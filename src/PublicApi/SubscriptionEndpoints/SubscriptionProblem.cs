using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Maps a <see cref="MaxioIntegrationException"/> onto an HTTP problem result:
/// caller-input failures surface as <c>400</c>, upstream Maxio faults as <c>502</c>.
/// </summary>
internal static class SubscriptionProblem
{
    public static IResult From(MaxioIntegrationException exception, string title)
    {
        var statusCode = exception.IsClientError
            ? StatusCodes.Status400BadRequest
            : StatusCodes.Status502BadGateway;

        return Results.Problem(detail: exception.Message, title: title, statusCode: statusCode);
    }
}

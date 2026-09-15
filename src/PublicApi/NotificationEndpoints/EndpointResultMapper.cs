using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.Infrastructure.Messaging;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

internal static class EndpointResultMapper
{
    public static IResult Map(OperationResult result) => result.Outcome switch
    {
        OperationOutcome.Success => Results.Ok(),
        OperationOutcome.NotFound => Results.NotFound(),
        OperationOutcome.Conflict => Results.Conflict(new { error = result.Error }),
        OperationOutcome.ProviderUnavailable => Results.Json(new { error = result.Error }, statusCode: StatusCodes.Status503ServiceUnavailable),
        _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
    };
}

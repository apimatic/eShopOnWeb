using System.Net;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Maps Maxio API failures to HTTP results without leaking internals beyond the upstream error message.
/// </summary>
public static class MaxioErrorMapper
{
    public static IResult ToErrorResult(MaxioApiException exception) => exception.StatusCode switch
    {
        HttpStatusCode.NotFound => Results.NotFound(new { message = exception.ResponseBody }),
        HttpStatusCode.UnprocessableEntity => Results.UnprocessableEntity(new { message = exception.ResponseBody }),
        HttpStatusCode.Unauthorized => Results.Problem(
            detail: "The Maxio API rejected the configured credentials.",
            statusCode: (int)HttpStatusCode.BadGateway),
        _ => Results.Problem(detail: exception.Message, statusCode: (int)HttpStatusCode.BadGateway)
    };
}

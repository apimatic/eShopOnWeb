using System;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.Infrastructure.Services;

namespace Microsoft.eShopWeb.PublicApi;

internal static class EndpointProblem
{
    public static IResult From(Exception exception) => exception switch
    {
        OrderFlowNotFoundException => Results.NotFound(),
        OrderFlowValidationException validation => Results.Problem(
            validation.Message,
            statusCode: StatusCodes.Status400BadRequest),
        OrderFlowConflictException conflict => Results.Problem(
            conflict.Message,
            statusCode: StatusCodes.Status409Conflict),
        OrderFlowUnavailableException unavailable => Results.Problem(
            unavailable.Message,
            statusCode: StatusCodes.Status503ServiceUnavailable),
        InvalidOperationException invalidOperation => Results.Problem(
            invalidOperation.Message,
            statusCode: StatusCodes.Status409Conflict),
        _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
    };
}

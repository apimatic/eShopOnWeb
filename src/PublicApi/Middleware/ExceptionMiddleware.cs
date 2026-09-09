using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.Middleware;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;

    public ExceptionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext httpContext)
    {
        try
        {
            await _next(httpContext);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(httpContext, ex);        
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";

        var (statusCode, message) = Map(exception);
        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = statusCode,
            Message = message
        }.ToString());
    }

    private static (int StatusCode, string Message) Map(Exception exception)
    {
        switch (exception)
        {
            case PaymentResourceNotFoundException:
                return ((int)HttpStatusCode.NotFound, exception.Message);

            // A challenge is a distinct, actionable condition (it derives from PaymentGatewayException,
            // so it must be matched before it).
            case PaymentChallengeRequiredException:
                return ((int)HttpStatusCode.Conflict, exception.Message);

            case PaymentConflictException:
            case DuplicateException:
                return ((int)HttpStatusCode.Conflict, exception.Message);

            case PaymentGatewayException gateway:
                return MapGateway(gateway);

            case UnauthorizedAccessException:
                return ((int)HttpStatusCode.Unauthorized, exception.Message);

            default:
                return ((int)HttpStatusCode.InternalServerError, exception.Message);
        }
    }

    private static (int StatusCode, string Message) MapGateway(PaymentGatewayException gateway)
    {
        var status = gateway.StatusCode.HasValue ? (int)gateway.StatusCode.Value : 0;

        // OUR credentials or OUR quota — the caller did nothing wrong and cannot fix it.
        if (status is 401 or 403 or 429)
        {
            return ((int)HttpStatusCode.BadGateway, "The payment provider is currently unavailable.");
        }

        // The provider rejected the caller's request with a status they can act on.
        if (status is >= 400 and < 500)
        {
            return (status, gateway.Message);
        }

        // A typed provider rejection with no HTTP status (e.g. a declined card) is still the
        // caller's to act on — surface it as Unprocessable Entity rather than a generic 502.
        if (gateway.DebugId is not null)
        {
            return ((int)HttpStatusCode.UnprocessableEntity, gateway.Message);
        }

        // Transport failure, provider 5xx, or unknown — no meaningful caller status.
        return ((int)HttpStatusCode.BadGateway, gateway.Message);
    }
}

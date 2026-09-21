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
            case DuplicateException:
                return ((int)HttpStatusCode.Conflict, exception.Message);
            case ResourceNotFoundException:
                return ((int)HttpStatusCode.NotFound, exception.Message);
            case ForbiddenException:
                return ((int)HttpStatusCode.Forbidden, exception.Message);
            case PaymentStateException:
                return ((int)HttpStatusCode.Conflict, exception.Message);
            case PaymentDeclinedException:
                return ((int)HttpStatusCode.PaymentRequired, exception.Message);
            case PayPalChallengeException:
                // Task rule: a challenge is surfaced, not handled with an approval round-trip.
                return ((int)HttpStatusCode.Conflict, exception.Message);
            case PayPalException paypal:
                // Auth/quota failures are ours (5xx); a provider rejection of the caller's request
                // (a decline, invalid data) is theirs to act on (4xx); transport/unknown → 5xx.
                if (paypal.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                    or HttpStatusCode.TooManyRequests)
                    return ((int)HttpStatusCode.BadGateway, "The payment provider is currently unavailable.");
                if (paypal.StatusCode is { } sc && (int)sc >= 400 && (int)sc < 500)
                    return ((int)sc, paypal.Message);
                if (paypal.ProviderErrorName is not null)
                    return ((int)HttpStatusCode.UnprocessableEntity, paypal.Message);
                return ((int)HttpStatusCode.BadGateway, paypal.Message);
            default:
                return ((int)HttpStatusCode.InternalServerError, exception.Message);
        }
    }
}

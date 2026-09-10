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

            case EntityNotFoundException:
                return ((int)HttpStatusCode.NotFound, exception.Message);

            // Business-rule violations and headless-only limitations are the caller's to act on.
            case PaymentDomainException:
            case AuthorizationRenewalException:
            case PaymentChallengeRequiredException:
                return ((int)HttpStatusCode.UnprocessableEntity, exception.Message);

            case PayPalGatewayException gatewayException:
                // A client-class error from PayPal (e.g. a declined card) is surfaced as 422;
                // a server-class error / timeout as 502 Bad Gateway.
                var status = gatewayException.StatusCode is >= 400 and < 500
                    ? (int)HttpStatusCode.UnprocessableEntity
                    : (int)HttpStatusCode.BadGateway;
                return (status, gatewayException.Message);

            default:
                return ((int)HttpStatusCode.InternalServerError, exception.Message);
        }
    }
}

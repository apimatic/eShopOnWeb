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

        var (statusCode, message) = MapException(exception);
        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = statusCode,
            Message = message
        }.ToString());
    }

    private static (int StatusCode, string Message) MapException(Exception exception)
    {
        switch (exception)
        {
            case DuplicateException:
                return ((int)HttpStatusCode.Conflict, exception.Message);

            case PaymentNotFoundException:
                return ((int)HttpStatusCode.NotFound, exception.Message);

            // A stale hold that can no longer be renewed — operator-actionable, not a server fault.
            case PaymentReauthorizationException:
                return ((int)HttpStatusCode.Conflict, exception.Message);

            // A domain rejection the caller can act on.
            case PaymentException:
                return ((int)HttpStatusCode.BadRequest, exception.Message);

            // A PayPal failure: pass a provider 4xx through as a client 4xx; otherwise a gateway (5xx) error.
            // The message is already caller-safe (built in the gateway) — never a raw provider/JSON string.
            case PaymentGatewayException gatewayException:
                var status = gatewayException.StatusCode is >= 400 and < 500
                    ? gatewayException.StatusCode.Value
                    : (int)HttpStatusCode.BadGateway;
                return (status, gatewayException.Message);

            default:
                return ((int)HttpStatusCode.InternalServerError, exception.Message);
        }
    }
}

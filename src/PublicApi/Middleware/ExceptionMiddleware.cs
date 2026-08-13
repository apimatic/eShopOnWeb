using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Messaging;

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

    // Maps domain and provider failures to caller-facing status codes. Every message reaching here is
    // caller-safe (it never contains a contact number).
    private static (int StatusCode, string Message) Map(Exception exception)
    {
        switch (exception)
        {
            case OrderNotFoundException:
            case NotificationNotFoundException:
                return ((int)HttpStatusCode.NotFound, exception.Message);

            case InvalidContactNumberException:
            case InvalidOrderRequestException:
                return ((int)HttpStatusCode.BadRequest, exception.Message);

            case DuplicateException:
            case InvalidOrderOperationException:
            case NotificationContentDisposedException:
                return ((int)HttpStatusCode.Conflict, exception.Message);

            case SmsProviderException sms:
                // A deterministic provider rejection (4xx) is surfaced as that client status; a
                // transport/unknown failure is an upstream outage (502).
                if (sms.StatusCode is >= 400 and < 500)
                {
                    return (sms.StatusCode.Value, sms.Message);
                }
                return ((int)HttpStatusCode.BadGateway, sms.Message);

            default:
                return ((int)HttpStatusCode.InternalServerError, exception.Message);
        }
    }
}

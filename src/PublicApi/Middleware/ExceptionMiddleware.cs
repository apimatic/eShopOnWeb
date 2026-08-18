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

    private static (int StatusCode, string Message) Map(Exception exception) => exception switch
    {
        DuplicateException => ((int)HttpStatusCode.Conflict, exception.Message),
        InvalidPhoneNumberException => ((int)HttpStatusCode.BadRequest, exception.Message),
        InvalidOrderRequestException => ((int)HttpStatusCode.BadRequest, exception.Message),
        OrderLifecycleException => ((int)HttpStatusCode.Conflict, exception.Message),
        SmsProviderException providerException => MapProvider(providerException),
        _ => ((int)HttpStatusCode.InternalServerError, exception.Message)
    };

    // The provider's message body is never surfaced (it can echo the destination number); only a
    // caller-safe message and a deliberately mapped status are returned.
    private static (int StatusCode, string Message) MapProvider(SmsProviderException exception)
    {
        var status = (int?)exception.StatusCode;
        return status switch
        {
            // Our credentials or our quota — the caller did nothing wrong and cannot fix it.
            401 or 403 => ((int)HttpStatusCode.BadGateway, "The messaging provider is unavailable."),
            429 => ((int)HttpStatusCode.ServiceUnavailable, "The messaging provider is temporarily unavailable."),
            // The provider rejected the caller's request — hand back the same class of status.
            >= 400 and < 500 => (status.Value, exception.Message),
            // Transport, timeout, or provider 5xx — no meaningful caller status.
            _ => ((int)HttpStatusCode.BadGateway, exception.Message)
        };
    }
}

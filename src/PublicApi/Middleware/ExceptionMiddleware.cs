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

        // The caller referenced an order or saved card that is not theirs / does not exist.
        OrderNotFoundException or PaymentMethodNotFoundException =>
            ((int)HttpStatusCode.NotFound, exception.Message),

        // A payment operation was requested in a state where it is not valid (e.g. refund before capture,
        // refund beyond captured amount, pay an already-paid order).
        PaymentStateException => ((int)HttpStatusCode.Conflict, exception.Message),

        // A challenge that would require a browser: reported, not worked around.
        BrowserApprovalRequiredException => ((int)HttpStatusCode.UnprocessableEntity, exception.Message),

        // A card decline surfaces the provider's own reason to the caller; a transport/config failure
        // is an upstream problem, not the caller's.
        PaymentGatewayException gatewayException =>
            (gatewayException.ProviderCode is not null
                ? (int)HttpStatusCode.UnprocessableEntity
                : (int)HttpStatusCode.BadGateway, gatewayException.Message),

        System.ArgumentException => ((int)HttpStatusCode.BadRequest, exception.Message),

        _ => ((int)HttpStatusCode.InternalServerError, exception.Message)
    };
}

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

        var (statusCode, message) = exception switch
        {
            DuplicateException duplicationException => ((int)HttpStatusCode.Conflict, duplicationException.Message),
            InvalidContactNumberException invalid => ((int)HttpStatusCode.BadRequest, invalid.Message),
            EmptyBasketOnCheckoutException empty => ((int)HttpStatusCode.BadRequest, empty.Message),
            UnauthorizedAccessException => ((int)HttpStatusCode.Unauthorized, "The caller is not authenticated."),
            OrderLifecycleException lifecycle when lifecycle.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)
                => ((int)HttpStatusCode.NotFound, lifecycle.Message),
            OrderLifecycleException lifecycle => ((int)HttpStatusCode.Conflict, lifecycle.Message),
            SmsProviderException provider when provider.StatusCode is 401 or 403
                => (502, "Provider unavailable."),
            SmsProviderException provider when provider.StatusCode is 429
                => (503, "Temporarily unavailable."),
            SmsProviderException provider when provider.StatusCode is >= 400 and < 500
                => (provider.StatusCode.Value, provider.Message),
            SmsProviderException provider => (502, provider.Message),
            _ => ((int)HttpStatusCode.InternalServerError, exception.Message)
        };

        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }
}

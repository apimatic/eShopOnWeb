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

        var (status, message) = exception switch
        {
            DuplicateException duplicationException => ((int)HttpStatusCode.Conflict, duplicationException.Message),
            InvalidContactNumberException invalid => ((int)HttpStatusCode.BadRequest, invalid.Message),
            ContactNumberNotFoundException => ((int)HttpStatusCode.NotFound, exception.Message),
            OrderNotFoundException => ((int)HttpStatusCode.NotFound, exception.Message),
            NotificationNotFoundException => ((int)HttpStatusCode.NotFound, exception.Message),
            InvalidOrderStateException invalidState => ((int)HttpStatusCode.Conflict, invalidState.Message),
            EmptyBasketOnCheckoutException empty => ((int)HttpStatusCode.BadRequest, empty.Message),
            UnauthorizedAccessException => ((int)HttpStatusCode.Unauthorized, "The caller is not authenticated."),
            ArgumentException argument => ((int)HttpStatusCode.BadRequest, argument.Message),
            MessagingProviderException provider when (int?)provider.StatusCode is 401 or 403 =>
                ((int)HttpStatusCode.BadGateway, "Provider unavailable."),
            MessagingProviderException provider when (int?)provider.StatusCode is 429 =>
                ((int)HttpStatusCode.ServiceUnavailable, "Temporarily unavailable."),
            MessagingProviderException provider when (int?)provider.StatusCode is >= 400 and < 500 =>
                ((int)provider.StatusCode!, provider.Message),
            MessagingProviderException provider => ((int)HttpStatusCode.BadGateway, provider.Message),
            _ => ((int)HttpStatusCode.InternalServerError, "An unexpected error occurred.")
        };

        context.Response.StatusCode = status;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }
}

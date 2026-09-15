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
            InvalidOrderStateException invalidState => ((int)HttpStatusCode.Conflict, invalidState.Message),
            EntityNotFoundException notFound => ((int)HttpStatusCode.NotFound, notFound.Message),
            ContactNumberRejectedException rejected => ((int)HttpStatusCode.BadRequest, rejected.Message),
            EmptyBasketOnCheckoutException empty => ((int)HttpStatusCode.BadRequest, empty.Message),
            SmsProviderException provider when (int?)provider.StatusCode is 401 or 403 =>
                ((int)HttpStatusCode.BadGateway, "The messaging provider is unavailable."),
            SmsProviderException provider when (int?)provider.StatusCode is 429 =>
                ((int)HttpStatusCode.ServiceUnavailable, "The messaging provider is temporarily unavailable."),
            SmsProviderException provider when (int?)provider.StatusCode is >= 400 and < 500 =>
                ((int)provider.StatusCode!, provider.Message),
            SmsProviderException provider => ((int)HttpStatusCode.BadGateway, provider.Message),
            _ => ((int)HttpStatusCode.InternalServerError, "An error occurred.")
        };

        context.Response.StatusCode = status;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = status,
            Message = message
        }.ToString());
    }
}

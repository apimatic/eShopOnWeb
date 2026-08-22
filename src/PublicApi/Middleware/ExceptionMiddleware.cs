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
            ContactNumberNotFoundException notFound => ((int)HttpStatusCode.NotFound, notFound.Message),
            OrderNotFoundException notFound => ((int)HttpStatusCode.NotFound, notFound.Message),
            NotificationNotFoundException notFound => ((int)HttpStatusCode.NotFound, notFound.Message),
            CatalogItemNotFoundException notFound => ((int)HttpStatusCode.BadRequest, notFound.Message),
            EmptyBasketOnCheckoutException empty => ((int)HttpStatusCode.BadRequest, empty.Message),
            ArgumentOutOfRangeException argument => ((int)HttpStatusCode.BadRequest, argument.Message),
            ArgumentException argument => ((int)HttpStatusCode.BadRequest, argument.Message),
            InvalidOperationException invalid => ((int)HttpStatusCode.Conflict, invalid.Message),
            SmsProviderException provider when (int?)provider.ProviderStatusCode is 401 or 403 =>
                ((int)HttpStatusCode.BadGateway, "The messaging provider is unavailable."),
            SmsProviderException provider when (int?)provider.ProviderStatusCode is >= 400 and < 500 =>
                ((int)provider.ProviderStatusCode!, provider.Message),
            SmsProviderException provider => ((int)HttpStatusCode.BadGateway, provider.Message),
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

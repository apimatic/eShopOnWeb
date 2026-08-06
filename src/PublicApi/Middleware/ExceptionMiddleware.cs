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

            // A shopper's order/card was not found (or isn't theirs).
            case OrderNotFoundException:
                return ((int)HttpStatusCode.NotFound, exception.Message);

            // Payment could not be completed (declined card, invalid saved card, business rule).
            case PaymentException:
            case CatalogItemNotFoundException:
            case EmptyBasketOnCheckoutException:
            case ArgumentException: // includes Ardalis Guard clause failures on bad input
                return ((int)HttpStatusCode.BadRequest, exception.Message);

            // PayPal was unreachable or returned an upstream failure.
            case PayPalApiException:
                return ((int)HttpStatusCode.BadGateway, exception.Message);

            default:
                return ((int)HttpStatusCode.InternalServerError, exception.Message);
        }
    }
}

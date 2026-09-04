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
        context.Response.StatusCode = (int)statusCode;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }

    private static (HttpStatusCode, string) Map(Exception exception)
    {
        switch (exception)
        {
            case DuplicateException duplicationException:
                return (HttpStatusCode.Conflict, duplicationException.Message);
            case OrderStateException stateException:
                // The message is written for the person who can act on it (shopper or operator).
                return (HttpStatusCode.Conflict, stateException.Message);
            case OrderNotFoundException notFound:
                return (HttpStatusCode.NotFound, notFound.Message);
            case PaymentMethodNotFoundException methodNotFound:
                return (HttpStatusCode.NotFound, methodNotFound.Message);
            case PaymentDeclinedException declined:
                return (HttpStatusCode.PaymentRequired, declined.Message);
            case PayPalGatewayException gatewayException:
                return (HttpStatusCode.BadGateway, gatewayException.Message);
            case ArgumentException argumentException:
                return (HttpStatusCode.BadRequest, argumentException.Message);
            default:
                return (HttpStatusCode.InternalServerError, exception.Message);
        }
    }
}

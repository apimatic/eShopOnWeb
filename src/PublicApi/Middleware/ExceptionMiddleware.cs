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

        switch (exception)
        {
            case DuplicateException duplicationException:
                context.Response.StatusCode = (int)HttpStatusCode.Conflict;
                await WriteError(context, duplicationException.Message);
                break;
            case OrderNotFoundException orderNotFound:
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                await WriteError(context, orderNotFound.Message);
                break;
            case SavedPaymentMethodNotFoundException paymentMethodNotFound:
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                await WriteError(context, paymentMethodNotFound.Message);
                break;
            case PaymentStateException paymentState:
                context.Response.StatusCode = (int)HttpStatusCode.Conflict;
                await WriteError(context, paymentState.Message);
                break;
            case AuthorizationRenewalException renewal:
                // Unprocessable here, but worded so an operator can act on it.
                context.Response.StatusCode = (int)HttpStatusCode.UnprocessableEntity;
                await WriteError(context, renewal.Message);
                break;
            case PaymentGatewayException gateway:
                context.Response.StatusCode = gateway.IsDecline
                    ? (int)HttpStatusCode.UnprocessableEntity
                    : (int)HttpStatusCode.BadGateway;
                await WriteError(context, gateway.Message);
                break;
            default:
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                await WriteError(context, exception.Message);
                break;
        }
    }

    private static async Task WriteError(HttpContext context, string message)
    {
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }
}

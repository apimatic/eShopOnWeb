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
            case OrderNotFoundException notFoundException:
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                await WriteError(context, notFoundException.Message);
                break;
            case OrderStateException stateException:
                context.Response.StatusCode = (int)HttpStatusCode.Conflict;
                await WriteError(context, stateException.Message);
                break;
            case AuthorizationNotRenewableException notRenewableException:
                context.Response.StatusCode = (int)HttpStatusCode.Conflict;
                await WriteError(context, notRenewableException.Message);
                break;
            case BuyerActionRequiredException buyerActionException:
                context.Response.StatusCode = (int)HttpStatusCode.UnprocessableEntity;
                await WriteError(context, buyerActionException.Message);
                break;
            case PaymentGatewayException gatewayException:
                // Provider 4xx rejections keep their status (the caller can act on them);
                // transport failures and unknowns are 502 — the provider side is at fault.
                context.Response.StatusCode = gatewayException.IsProviderRejection
                    ? gatewayException.ProviderStatusCode!.Value
                    : (int)HttpStatusCode.BadGateway;
                await WriteError(context, gatewayException.Message);
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

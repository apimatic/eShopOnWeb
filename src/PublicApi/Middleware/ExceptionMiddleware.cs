using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using ArgumentException = System.ArgumentException;

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
                await context.Response.WriteAsync(new ErrorDetails()
                {
                    StatusCode = context.Response.StatusCode,
                    Message = duplicationException.Message
                }.ToString());
                break;

            case OrderPaymentStateException stateException:
                // The order/payment isn't in a state that allows this action (e.g. fulfilling an
                // unauthorized order, or an authorization PayPal will no longer renew).
                context.Response.StatusCode = (int)HttpStatusCode.Conflict;
                await context.Response.WriteAsync(new ErrorDetails()
                {
                    StatusCode = context.Response.StatusCode,
                    Message = stateException.Message
                }.ToString());
                break;

            case PaymentGatewayException gatewayException:
                // A transient/transport failure from PayPal is a 502 (retry may succeed); a business
                // rejection (declined card, expired auth, invalid refund, etc.) is a 422 the caller can't
                // fix by retrying as-is.
                context.Response.StatusCode = gatewayException.IsRetryable
                    ? (int)HttpStatusCode.BadGateway
                    : (int)HttpStatusCode.UnprocessableEntity;
                await context.Response.WriteAsync(new ErrorDetails()
                {
                    StatusCode = context.Response.StatusCode,
                    Message = gatewayException.ErrorCode is null
                        ? gatewayException.Message
                        : $"{gatewayException.Message} [{gatewayException.ErrorCode}]"
                }.ToString());
                break;

            case ArgumentException argumentException:
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                await context.Response.WriteAsync(new ErrorDetails()
                {
                    StatusCode = context.Response.StatusCode,
                    Message = argumentException.Message
                }.ToString());
                break;

            default:
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                await context.Response.WriteAsync(new ErrorDetails()
                {
                    StatusCode = context.Response.StatusCode,
                    Message = exception.Message
                }.ToString());
                break;
        }
    }
}

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

        // Caller referenced something that doesn't exist for them (or belongs to another shopper).
        PaymentEntityNotFoundException => ((int)HttpStatusCode.NotFound, exception.Message),

        // Malformed request.
        InvalidPaymentRequestException => ((int)HttpStatusCode.BadRequest, exception.Message),

        // Invalid state transition or refund exceeding capture; a stale hold that can't be renewed.
        AuthorizationNotRenewableException => ((int)HttpStatusCode.Conflict, exception.Message),
        PaymentConflictException => ((int)HttpStatusCode.Conflict, exception.Message),

        // PayPal asked for a browser approval this integration doesn't support.
        PaymentApprovalRequiredException => ((int)HttpStatusCode.PaymentRequired, exception.Message),

        // Any other PayPal failure: surface a client 4xx as-is, otherwise a 502 (upstream) — never
        // leak SDK/framework detail (PaymentGatewayException.Message is already caller-safe).
        PaymentGatewayException gateway => (
            gateway.ProviderStatusCode is >= 400 and < 500 ? gateway.ProviderStatusCode.Value : (int)HttpStatusCode.BadGateway,
            gateway.Message),

        _ => ((int)HttpStatusCode.InternalServerError, exception.Message)
    };
}

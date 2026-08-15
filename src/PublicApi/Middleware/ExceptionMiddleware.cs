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

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";

        var statusCode = exception switch
        {
            DuplicateException => HttpStatusCode.Conflict,
            EntityNotFoundException => HttpStatusCode.NotFound,
            // Payer/3DS approval required, or a hold that can no longer be renewed: both are terminal
            // conditions an operator/shopper must act on, surfaced with the actionable message.
            PayPalChallengeRequiredException => HttpStatusCode.Conflict,
            AuthorizationNotRenewableException => HttpStatusCode.Conflict,
            // A wrong-state payment operation (e.g. refund before fulfilment) is a client error.
            PaymentStateException => HttpStatusCode.Conflict,
            // Any other PayPal failure is an upstream/gateway problem.
            PayPalGatewayException => HttpStatusCode.BadGateway,
            UnauthorizedAccessException => HttpStatusCode.Unauthorized,
            _ => HttpStatusCode.InternalServerError
        };

        context.Response.StatusCode = (int)statusCode;
        await context.Response.WriteAsync(new ErrorDetails
        {
            StatusCode = context.Response.StatusCode,
            Message = exception.Message
        }.ToString());
    }
}

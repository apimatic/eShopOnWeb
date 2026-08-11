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

        var statusCode = exception switch
        {
            DuplicateException => HttpStatusCode.Conflict,
            // Owner-scoped "not found" for orders and saved cards, and missing catalog items.
            PaymentResourceNotFoundException => HttpStatusCode.NotFound,
            // Invalid state transitions / validation on payment operations.
            PaymentOperationException => HttpStatusCode.Conflict,
            // A stale hold that could not be renewed — operator-actionable.
            AuthorizationNotRenewableException => HttpStatusCode.Conflict,
            // PayPal asked for a browser approval we deliberately do not build.
            PaymentChallengeRequiredException => HttpStatusCode.Conflict,
            // PayPal rejected the call; surface as a gateway error.
            PayPalApiException => HttpStatusCode.BadGateway,
            UnauthorizedAccessException => HttpStatusCode.Unauthorized,
            _ => HttpStatusCode.InternalServerError
        };

        context.Response.StatusCode = (int)statusCode;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = exception.Message
        }.ToString());
    }
}

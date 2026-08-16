using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
// OrderNotFoundException, PaymentStateException, PaymentGatewayException, etc. live in the Exceptions namespace above.

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

        var (statusCode, message) = exception switch
        {
            OrderNotFoundException => (HttpStatusCode.NotFound, exception.Message),
            OrderPlacementException => (HttpStatusCode.BadRequest, exception.Message),
            PaymentStateException => (HttpStatusCode.BadRequest, exception.Message),
            PaymentApprovalRequiredException => (HttpStatusCode.BadRequest, exception.Message),
            AuthorizationNotRenewableException => (HttpStatusCode.Conflict, exception.Message),
            DuplicateException => (HttpStatusCode.Conflict, exception.Message),
            // A failure returned by PayPal: surface it as an upstream (bad gateway) error, not a 500.
            PaymentGatewayException gatewayException => (HttpStatusCode.BadGateway, DescribeGatewayError(gatewayException)),
            _ => (HttpStatusCode.InternalServerError, exception.Message)
        };

        context.Response.StatusCode = (int)statusCode;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }

    private static string DescribeGatewayError(PaymentGatewayException exception)
    {
        var suffix = string.IsNullOrEmpty(exception.DebugId) ? string.Empty : $" (PayPal debug id: {exception.DebugId})";
        return $"{exception.Message}{suffix}";
    }
}

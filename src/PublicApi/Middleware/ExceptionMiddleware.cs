using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;

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
            OrderNotFoundException => HttpStatusCode.NotFound,
            PaymentMethodNotFoundException => HttpStatusCode.NotFound,
            DuplicateException => HttpStatusCode.Conflict,
            InvalidPaymentOperationException => HttpStatusCode.Conflict,
            AuthorizationNotRenewableException => HttpStatusCode.Conflict,
            PaymentApprovalRequiredException => HttpStatusCode.BadRequest,
            // A failure reported by PayPal itself: surface as a bad gateway so operators can tell it
            // apart from our own faults. The message carries PayPal's name/issue detail.
            PayPalApiException => HttpStatusCode.BadGateway,
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

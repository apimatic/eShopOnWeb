using System;
using System.Collections.Generic;
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

        var (statusCode, message) = exception switch
        {
            DuplicateException => (HttpStatusCode.Conflict, exception.Message),
            KeyNotFoundException => (HttpStatusCode.NotFound, exception.Message),
            // The shopper must approve in a browser — reported, not worked around.
            PaymentApprovalRequiredException => (HttpStatusCode.UnprocessableEntity, exception.Message),
            // A business-rule violation stated in terms the caller can act on.
            PaymentException => (HttpStatusCode.BadRequest, exception.Message),
            // PayPal rejected the request; surface its reason as a bad gateway.
            PayPalApiException payPalEx => (HttpStatusCode.BadGateway,
                $"PayPal error ({payPalEx.Issue ?? payPalEx.StatusCode.ToString()}): {payPalEx.Message}"),
            _ => (HttpStatusCode.InternalServerError, exception.Message)
        };

        context.Response.StatusCode = (int)statusCode;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }
}

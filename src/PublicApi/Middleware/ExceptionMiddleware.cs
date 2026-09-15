using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
// OrderPaymentException, PaymentApprovalRequiredException and PaymentGatewayException live in the
// same ApplicationCore.Exceptions namespace imported above.

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
            // Payment/fulfilment state conflicts (e.g. fulfilling an unauthorized order, over-refunding).
            OrderPaymentException => HttpStatusCode.Conflict,
            // A card payment that would need a browser approval step this integration does not build.
            PaymentApprovalRequiredException => HttpStatusCode.Conflict,
            // The upstream processor rejected the request.
            PaymentGatewayException => HttpStatusCode.BadGateway,
            // Guard-clause / argument validation failures on request input.
            ArgumentException => HttpStatusCode.BadRequest,
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

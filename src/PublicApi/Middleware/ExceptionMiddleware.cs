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

        var (statusCode, message) = exception switch
        {
            DuplicateException dup => ((int)HttpStatusCode.Conflict, dup.Message),
            OrderNotFoundException nf => ((int)HttpStatusCode.NotFound, nf.Message),
            PaymentMethodNotFoundException nf => ((int)HttpStatusCode.NotFound, nf.Message),
            PaymentValidationException val => ((int)HttpStatusCode.BadRequest, val.Message),
            InvalidPaymentOperationException inv => ((int)HttpStatusCode.Conflict, inv.Message),
            // A payment provider rejection (e.g. a declined card) is a failed payment, not a server error.
            PaymentGatewayException pay => ((int)HttpStatusCode.PaymentRequired, pay.Message),
            _ => ((int)HttpStatusCode.InternalServerError, exception.Message)
        };

        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = statusCode,
            Message = message
        }.ToString());
    }
}

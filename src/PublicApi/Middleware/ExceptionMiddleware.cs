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

        // Map each failure kind to a coherent status and a caller-safe message. Payment exceptions carry
        // messages built to be safe to return (no card data, no SDK/JSON internals).
        var (statusCode, message) = exception switch
        {
            PaymentNotFoundException => ((int)HttpStatusCode.NotFound, exception.Message),
            PaymentValidationException => ((int)HttpStatusCode.BadRequest, exception.Message),
            PaymentConflictException => ((int)HttpStatusCode.Conflict, exception.Message),
            // Covers PaymentGatewayException and its subclasses (challenge-required, reauthorization-expired),
            // each of which carries the client status the caller should see.
            PaymentGatewayException gateway => (gateway.ClientStatusCode, gateway.Message),
            DuplicateException => ((int)HttpStatusCode.Conflict, exception.Message),
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

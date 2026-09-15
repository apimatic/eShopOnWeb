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
            DuplicateException => (int)HttpStatusCode.Conflict,                       // 409
            OrderNotFoundException => (int)HttpStatusCode.NotFound,                   // 404
            PaymentChallengeRequiredException => (int)HttpStatusCode.UnprocessableEntity, // 422
            PaymentException => (int)HttpStatusCode.UnprocessableEntity,              // 422 — caller/operator actionable
            PaymentGatewayException => (int)HttpStatusCode.BadGateway,                // 502 — PayPal-side failure
            _ => (int)HttpStatusCode.InternalServerError,                             // 500
        };

        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsync(new ErrorDetails
        {
            StatusCode = statusCode,
            Message = exception.Message,
        }.ToString());
    }
}

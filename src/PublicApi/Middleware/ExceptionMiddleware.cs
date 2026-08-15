using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

// PaymentException and friends live in Microsoft.eShopWeb.ApplicationCore.Exceptions (imported above).

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

        // Map application exceptions to caller-actionable status codes. Payment-provider failures
        // become 502 (a retry is meaningful); caller/card rejections become 400; ownership/not-found
        // becomes 404; identity problems become 401.
        var (statusCode, message) = exception switch
        {
            DuplicateException => (HttpStatusCode.Conflict, exception.Message),
            PaymentNotFoundException => (HttpStatusCode.NotFound, exception.Message),
            PaymentProviderUnavailableException => (HttpStatusCode.BadGateway, exception.Message),
            PaymentChallengeRequiredException => (HttpStatusCode.BadRequest, exception.Message),
            PaymentException => (HttpStatusCode.BadRequest, exception.Message),
            UnauthorizedAccessException => (HttpStatusCode.Unauthorized, exception.Message),
            ArgumentException => (HttpStatusCode.BadRequest, exception.Message),
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

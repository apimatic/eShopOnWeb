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

        // Map known domain failures to actionable status codes. The messages on these exceptions are
        // curated, caller-safe strings (see the ApplicationCore payment exceptions).
        var (statusCode, message) = exception switch
        {
            EntityNotFoundException => (HttpStatusCode.NotFound, exception.Message),
            InvalidOrderStateException => (HttpStatusCode.Conflict, exception.Message),
            PaymentRejectedException => (HttpStatusCode.UnprocessableEntity, exception.Message),
            PaymentProviderException => (HttpStatusCode.BadGateway, exception.Message),
            DuplicateException => (HttpStatusCode.Conflict, exception.Message),
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

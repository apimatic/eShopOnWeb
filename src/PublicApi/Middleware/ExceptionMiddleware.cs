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

        var (statusCode, message) = exception switch
        {
            DuplicateException => ((int)HttpStatusCode.Conflict, exception.Message),
            InvoiceNotFoundException => ((int)HttpStatusCode.NotFound, exception.Message),
            OrderNotFoundException => ((int)HttpStatusCode.NotFound, exception.Message),
            InvalidOrderRequestException => ((int)HttpStatusCode.BadRequest, exception.Message),
            InvoiceStateException => ((int)HttpStatusCode.Conflict, exception.Message),
            // A provider outcome carries the status the caller should see (e.g. a refused transition).
            InvoiceProviderException providerException => (providerException.SuggestedStatusCode, providerException.Message),
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

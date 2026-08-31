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
            DuplicateException => HttpStatusCode.Conflict,
            OrderNotFoundException => HttpStatusCode.NotFound,
            InvoiceNotFoundException => HttpStatusCode.NotFound,
            InvoiceAccessDeniedException => HttpStatusCode.Forbidden,
            // A refusal driven by the state the bill is in (e.g. correcting an issued/withdrawn bill).
            InvoiceStateException => HttpStatusCode.Conflict,
            // The provider could not be reached or errored unexpectedly.
            InvoicingProviderException => HttpStatusCode.BadGateway,
            // Invalid input surfaced from the domain (e.g. unknown catalog item, empty order).
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

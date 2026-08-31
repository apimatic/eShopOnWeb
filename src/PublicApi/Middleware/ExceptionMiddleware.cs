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

        var (statusCode, message) = MapException(exception);
        context.Response.StatusCode = (int)statusCode;

        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }

    private static (HttpStatusCode statusCode, string message) MapException(Exception exception) => exception switch
    {
        // A bill or order that does not exist, or is not the caller's to see.
        InvoiceNotFoundException or OrderNotFoundException =>
            (HttpStatusCode.NotFound, exception.Message),

        // A change that the bill's current state does not allow (already issued, already withdrawn):
        // an expected outcome of the bill's lifecycle, reported rather than silently ignored.
        InvoiceStateException =>
            (HttpStatusCode.Conflict, exception.Message),

        // The provider refused the request on state grounds (4xx) vs. failed to serve it (5xx / transport).
        VisaInvoicingException visaException =>
            (visaException.IsProviderRefusal ? HttpStatusCode.Conflict : HttpStatusCode.BadGateway,
             DescribeVisaFailure(visaException)),

        DuplicateException =>
            (HttpStatusCode.Conflict, exception.Message),

        _ => (HttpStatusCode.InternalServerError, exception.Message)
    };

    private static string DescribeVisaFailure(VisaInvoicingException exception) =>
        string.IsNullOrWhiteSpace(exception.ProviderReason)
            ? exception.Message
            : $"{exception.Message} Provider detail: {exception.ProviderReason}";
}

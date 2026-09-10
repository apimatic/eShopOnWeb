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

        if (exception is DuplicateException duplicationException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = duplicationException.Message
            }.ToString());
        }
        else if (exception is BillingException billingException)
        {
            context.Response.StatusCode = MapBillingStatusCode(billingException);
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = billingException.Message
            }.ToString());
        }
        else
        {
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = exception.Message
            }.ToString());
        }
    }

    /// <summary>
    /// Maps a billing failure to a caller-facing status. Provider failures that are OUR fault
    /// (bad credentials, spent quota) or transport/unknown failures become 5xx — never handed back
    /// to the caller as their mistake; genuinely caller-fixable provider 4xx (e.g. a bad plan
    /// handle, a validation rejection) are passed through.
    /// </summary>
    private static int MapBillingStatusCode(BillingException exception) => exception.ProviderStatusCode switch
    {
        401 or 403 => (int)HttpStatusCode.BadGateway,          // our credentials — not the caller's fault
        429 => (int)HttpStatusCode.ServiceUnavailable,          // our quota — not the caller's fault
        >= 400 and < 500 => exception.ProviderStatusCode.Value, // caller-fixable (404 / 409 / 422 / 400)
        _ => (int)HttpStatusCode.BadGateway,                    // transport, 5xx, or unknown
    };
}

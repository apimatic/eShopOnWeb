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
            // Single coherent ladder for the subscription-billing integration. The message is
            // already caller-safe; a validation failure is the caller's to fix, our credentials /
            // quota or a provider outage are not (surfaced as 5xx).
            context.Response.StatusCode = billingException.Kind switch
            {
                BillingErrorKind.Validation => (int)HttpStatusCode.BadRequest,
                BillingErrorKind.NotFound => (int)HttpStatusCode.NotFound,
                BillingErrorKind.ProviderUnavailable => (int)HttpStatusCode.BadGateway,
                _ => (int)HttpStatusCode.InternalServerError
            };

            var message = billingException.Errors.Count > 0
                ? $"{billingException.Message} {string.Join("; ", billingException.Errors)}"
                : billingException.Message;

            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = message
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
}

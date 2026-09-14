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
        else if (exception is BillingProviderException billingException)
        {
            // One ladder for every billing failure, so the same kind of failure always answers the same
            // status. The message is the exception's own caller-safe text; the provider's own status and
            // body stay in the logs.
            context.Response.StatusCode = ToStatusCode(billingException.Failure);
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = billingException.ToCallerMessage()
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

    private static int ToStatusCode(BillingFailure failure) => failure switch
    {
        // The caller sent something the billing system would not accept, and can fix it.
        BillingFailure.InvalidRequest => (int)HttpStatusCode.BadRequest,
        BillingFailure.NotFound => (int)HttpStatusCode.NotFound,
        BillingFailure.Conflict => (int)HttpStatusCode.Conflict,
        // Transient on the provider's side: worth retrying.
        BillingFailure.Unavailable => (int)HttpStatusCode.ServiceUnavailable,
        // Our own misconfiguration - never reported as the caller's fault, and never retryable.
        BillingFailure.Configuration => (int)HttpStatusCode.InternalServerError,
        _ => (int)HttpStatusCode.BadGateway
    };
}

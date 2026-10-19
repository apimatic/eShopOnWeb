using System;
using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.Middleware;

public class ExceptionMiddleware
{
    // application/json body, never embedded in HTML: plain apostrophes/quotes in messages are fine.
    private static readonly JsonSerializerOptions BillingErrorJsonOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

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

        if (exception is BillingException billingException)
        {
            context.Response.StatusCode = billingException.Kind switch
            {
                BillingFailureKind.InvalidRequest => (int)HttpStatusCode.BadRequest,
                BillingFailureKind.Rejected => (int)HttpStatusCode.UnprocessableEntity,
                BillingFailureKind.Conflict => (int)HttpStatusCode.Conflict,
                _ => (int)HttpStatusCode.ServiceUnavailable
            };
            if (billingException.Kind is BillingFailureKind.Conflict or BillingFailureKind.Unavailable)
            {
                context.Response.Headers.RetryAfter = "5";
            }

            // Only the caller-safe message and provider validation messages; never SDK/exception internals.
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                StatusCode = context.Response.StatusCode,
                billingException.Message,
                billingException.Errors
            }, BillingErrorJsonOptions));
        }
        else if (exception is DuplicateException duplicationException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = duplicationException.Message
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

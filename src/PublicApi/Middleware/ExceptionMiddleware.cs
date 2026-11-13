using System;
using System.Net;
using System.Text.Json;
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

        if (exception is BillingException billingException)
        {
            context.Response.StatusCode = billingException.Failure switch
            {
                BillingFailure.InvalidPlan => (int)HttpStatusCode.BadRequest,
                BillingFailure.InProgress => (int)HttpStatusCode.Conflict,
                BillingFailure.Rejected => (int)HttpStatusCode.UnprocessableEntity,
                BillingFailure.ProviderMisconfigured => (int)HttpStatusCode.BadGateway,
                BillingFailure.ProviderUnavailable => (int)HttpStatusCode.ServiceUnavailable,
                BillingFailure.OutcomeUnknown => (int)HttpStatusCode.ServiceUnavailable,
                _ => (int)HttpStatusCode.InternalServerError
            };
            if (billingException.Failure is BillingFailure.ProviderUnavailable or BillingFailure.OutcomeUnknown or BillingFailure.InProgress)
            {
                context.Response.Headers.RetryAfter = "5";
            }
            // Message and details are caller-safe by construction; provider internals stay in InnerException.
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                StatusCode = context.Response.StatusCode,
                Message = billingException.Message,
                Code = billingException.Failure.ToString(),
                Details = billingException.Details,
                Reference = billingException.Reference
            }));
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

using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Middleware;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
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
            var statusCode = billingException.Kind switch
            {
                BillingErrorKind.InvalidRequest => StatusCodes.Status400BadRequest,
                BillingErrorKind.NotFound => StatusCodes.Status404NotFound,
                BillingErrorKind.Conflict => StatusCodes.Status409Conflict,
                BillingErrorKind.Validation => StatusCodes.Status422UnprocessableEntity,
                BillingErrorKind.InvalidProviderResponse => StatusCodes.Status502BadGateway,
                _ => StatusCodes.Status503ServiceUnavailable
            };
            _logger.LogWarning(
                "Subscription billing request failed with {BillingErrorKind}; trace {TraceId}.",
                billingException.Kind,
                context.TraceIdentifier);
            context.Response.StatusCode = statusCode;
            await Results.Problem(
                    statusCode: statusCode,
                    title: "Subscription billing request failed",
                    detail: billingException.Message,
                    extensions: new System.Collections.Generic.Dictionary<string, object?>
                    {
                        ["traceId"] = context.TraceIdentifier
                    })
                .ExecuteAsync(context);
            return;
        }

        if (exception is DuplicateException duplicationException)
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
            _logger.LogError(exception, "Unhandled API exception; trace {TraceId}.", context.TraceIdentifier);
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = "An unexpected error occurred."
            }.ToString());
        }
    }
}

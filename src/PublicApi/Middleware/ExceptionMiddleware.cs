using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.PublicApi.Billing;
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
            if (ex is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
            {
                _logger.LogDebug("Request {TraceIdentifier} was canceled by the caller.", httpContext.TraceIdentifier);
                return;
            }

            await HandleExceptionAsync(httpContext, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/problem+json";

        string detail;
        if (exception is BillingException billingException)
        {
            context.Response.StatusCode = (int)billingException.StatusCode;
            detail = billingException.Message;
            _logger.LogWarning(
                exception,
                "Billing request {TraceIdentifier} failed with status {StatusCode}.",
                context.TraceIdentifier,
                context.Response.StatusCode);
        }
        else if (exception is DuplicateException duplicationException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            detail = duplicationException.Message;
        }
        else
        {
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            detail = "An unexpected error occurred.";
            _logger.LogError(exception, "Unhandled request failure {TraceIdentifier}.", context.TraceIdentifier);
        }

        var problem = new
        {
            type = "about:blank",
            title = ReasonPhrases.GetReasonPhrase(context.Response.StatusCode),
            status = context.Response.StatusCode,
            detail,
            traceId = context.TraceIdentifier
        };
        await context.Response.WriteAsync(JsonSerializer.Serialize(problem));
    }
}

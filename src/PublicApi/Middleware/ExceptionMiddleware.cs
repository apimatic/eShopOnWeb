using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
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
            _logger.LogWarning("Billing request {TraceIdentifier} failed with status {StatusCode}: {Title}",
                context.TraceIdentifier, (int)billingException.StatusCode, billingException.Title);
            context.Response.StatusCode = (int)billingException.StatusCode;
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = context.Response.StatusCode,
                Title = billingException.Title,
                Detail = billingException.Message,
                Instance = context.Request.Path,
                Extensions = { ["traceId"] = context.TraceIdentifier }
            });
        }
        else if (exception is DuplicateException duplicationException)
        {
            _logger.LogWarning("Request {TraceIdentifier} failed due to a duplicate resource",
                context.TraceIdentifier);
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = duplicationException.Message
            }.ToString());
        }
        else
        {
            _logger.LogError(exception, "Request {TraceIdentifier} failed", context.TraceIdentifier);
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = context.Response.StatusCode,
                Title = "An unexpected error occurred",
                Detail = "The request could not be completed.",
                Instance = context.Request.Path,
                Extensions = { ["traceId"] = context.TraceIdentifier }
            });
        }
    }
}

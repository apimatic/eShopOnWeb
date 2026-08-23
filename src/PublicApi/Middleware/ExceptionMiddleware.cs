using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
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

        if (exception is SubscriptionBillingException billingException)
        {
            context.Response.StatusCode = (int)billingException.StatusCode;
            _logger.LogWarning(
                billingException.InnerException,
                "Subscription billing request failed with status {StatusCode}; trace {TraceIdentifier}.",
                context.Response.StatusCode,
                context.TraceIdentifier);
            await WriteProblemAsync(
                context,
                billingException.Title,
                billingException.Message);
        }
        else if (exception is DuplicateException duplicationException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await WriteProblemAsync(context, "Conflict", duplicationException.Message);
        }
        else
        {
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            _logger.LogError(exception, "Unhandled request failure; trace {TraceIdentifier}.", context.TraceIdentifier);
            await WriteProblemAsync(
                context,
                "Unexpected server error",
                "An unexpected error occurred while processing the request.");
        }
    }

    private static Task WriteProblemAsync(HttpContext context, string title, string detail)
    {
        var problem = new ProblemDetails
        {
            Status = context.Response.StatusCode,
            Title = title,
            Detail = detail,
            Instance = context.Request.Path
        };
        problem.Extensions["traceId"] = context.TraceIdentifier;
        return context.Response.WriteAsync(JsonSerializer.Serialize(problem, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));
    }
}

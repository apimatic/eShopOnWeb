using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
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
        var (statusCode, message) = Describe(exception);

        if (statusCode >= (int)HttpStatusCode.InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception on {Method} {Path}.", context.Request.Method, context.Request.Path);
        }
        else
        {
            _logger.LogInformation("Request to {Method} {Path} rejected with {StatusCode}: {Message}",
                context.Request.Method, context.Request.Path, statusCode, message);
        }

        if (context.Response.HasStarted)
        {
            // The handler already began writing; anything more would corrupt the response.
            return;
        }

        context.Response.Clear();
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = statusCode;

        await context.Response.WriteAsync(new ErrorDetails
        {
            StatusCode = statusCode,
            Message = message
        }.ToString());
    }

    private static (int StatusCode, string Message) Describe(Exception exception) => exception switch
    {
        DuplicateException duplicate => ((int)HttpStatusCode.Conflict, duplicate.Message),

        // The plan handle the caller asked for is not in the catalog.
        PlanNotFoundException planNotFound => ((int)HttpStatusCode.NotFound, planNotFound.Message),

        // The billing system refused the request itself - actionable by the caller.
        BillingValidationException validation => ((int)HttpStatusCode.BadRequest, validation.Message),

        // A write is still in flight upstream; retrying is the right move.
        DuplicateSubscribeRequestException inFlight => ((int)HttpStatusCode.Conflict, inFlight.Message),

        // Anything else from the billing system is an upstream failure, not the caller's fault.
        BillingException billing => ((int)HttpStatusCode.BadGateway, billing.Message),

        _ => ((int)HttpStatusCode.InternalServerError, exception.Message)
    };
}

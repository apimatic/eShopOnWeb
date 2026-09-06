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
        var (statusCode, message) = Translate(exception);

        if (statusCode >= (int)HttpStatusCode.InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception handling {Method} {Path}.",
                context.Request.Method, context.Request.Path);
        }
        else
        {
            _logger.LogWarning(exception, "Request {Method} {Path} rejected with {StatusCode}.",
                context.Request.Method, context.Request.Path, statusCode);
        }

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = statusCode;

        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = statusCode,
            Message = message
        }.ToString());
    }

    private static (int StatusCode, string Message) Translate(Exception exception) => exception switch
    {
        DuplicateException duplicate => ((int)HttpStatusCode.Conflict, duplicate.Message),

        // The shopper named a plan that is not on offer - their request, their fix.
        SubscriptionPlanNotFoundException planNotFound => ((int)HttpStatusCode.BadRequest, planNotFound.Message),

        // A concurrent subscribe attempt is still settling; retrying shortly is safe.
        SubscriptionConflictException conflict => ((int)HttpStatusCode.Conflict, conflict.Message),

        // The billing system is the upstream dependency, so its failures are gateway failures.
        BillingGatewayException billing => ((int)HttpStatusCode.BadGateway, billing.Message),

        _ => ((int)HttpStatusCode.InternalServerError, exception.Message)
    };
}

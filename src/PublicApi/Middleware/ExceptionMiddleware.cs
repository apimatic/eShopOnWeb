using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

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

        var (statusCode, message) = Describe(exception);
        context.Response.StatusCode = (int)statusCode;

        // Without this the cause of a 5xx is lost: the client only ever sees the message.
        _logger.Log(statusCode >= HttpStatusCode.InternalServerError ? LogLevel.Error : LogLevel.Warning,
            exception, "{Method} {Path} failed with {StatusCode}.", context.Request.Method, context.Request.Path, (int)statusCode);

        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }

    private static (HttpStatusCode StatusCode, string Message) Describe(Exception exception) => exception switch
    {
        DuplicateException => (HttpStatusCode.Conflict, exception.Message),

        // The caller named a plan that is not in the catalogue: their request, not our failure.
        SubscriptionPlanNotFoundException => (HttpStatusCode.BadRequest, exception.Message),

        // The billing system understood the request and refused it on business rules.
        SubscriptionBillingValidationException => (HttpStatusCode.UnprocessableEntity, exception.Message),

        // The billing system could not be reached or failed: an upstream dependency problem, so the
        // caller is told it is worth retrying rather than being blamed for a bad request.
        SubscriptionBillingException => (HttpStatusCode.BadGateway, exception.Message),

        _ => (HttpStatusCode.InternalServerError, exception.Message)
    };
}

using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
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

        if (exception is DuplicateException duplicationException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = duplicationException.Message
            }.ToString());
        }
        else if (exception is SubscriptionRequestException)
        {
            await WriteProblemAsync(context, HttpStatusCode.BadRequest, exception.Message);
        }
        else if (exception is SubscriptionIdentityException)
        {
            await WriteProblemAsync(context, HttpStatusCode.Unauthorized, exception.Message);
        }
        else if (exception is SubscriptionPlanNotFoundException)
        {
            await WriteProblemAsync(context, HttpStatusCode.NotFound, exception.Message);
        }
        else if (exception is SubscriptionReferenceConflictException)
        {
            await WriteProblemAsync(context, HttpStatusCode.Conflict, exception.Message);
        }
        else if (exception is MaxioApiException maxioException)
        {
            _logger.LogWarning(maxioException, "A Maxio Advanced Billing request failed with status {StatusCode}.", maxioException.StatusCode);
            var status = maxioException.StatusCode == HttpStatusCode.UnprocessableEntity
                ? HttpStatusCode.UnprocessableEntity
                : HttpStatusCode.BadGateway;
            await WriteProblemAsync(context, status, maxioException.Message);
        }
        else
        {
            _logger.LogError(exception, "An unhandled PublicApi exception occurred.");
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = "An unexpected error occurred."
            }.ToString());
        }
    }

    private static Task WriteProblemAsync(HttpContext context, HttpStatusCode statusCode, string detail)
    {
        context.Response.StatusCode = (int)statusCode;
        return context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = (int)statusCode,
            Title = statusCode.ToString(),
            Detail = detail
        });
    }
}

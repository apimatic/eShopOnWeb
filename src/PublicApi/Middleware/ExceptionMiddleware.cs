using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.PublicApi.SubscriptionBilling;
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
        else if (exception is SubscriptionBillingException billingException)
        {
            context.Response.StatusCode = billingException.StatusCode;
            await context.Response.WriteAsJsonAsync(new
            {
                type = "https://httpstatuses.com/" + billingException.StatusCode,
                title = billingException.Title,
                status = billingException.StatusCode,
                detail = billingException.Message
            });
        }
        else if (exception is MaxioApiException maxioException)
        {
            _logger.LogError(maxioException, "Maxio request failed with status {StatusCode}", maxioException.StatusCode);
            var statusCode = maxioException.StatusCode == (int)HttpStatusCode.UnprocessableEntity
                ? (int)HttpStatusCode.UnprocessableEntity
                : (int)HttpStatusCode.BadGateway;
            context.Response.StatusCode = statusCode;
            await context.Response.WriteAsJsonAsync(new
            {
                type = "https://httpstatuses.com/" + statusCode,
                title = "Subscription billing request failed",
                status = statusCode,
                detail = maxioException.Errors.Count > 0
                    ? string.Join(" ", maxioException.Errors)
                    : maxioException.Message
            });
        }
        else
        {
            _logger.LogError(exception, "Unhandled request exception");
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = "An unexpected error occurred."
            }.ToString());
        }
    }
}

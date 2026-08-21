using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Billing;
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
        if (exception is SubscriptionBillingException billingException)
        {
            var statusCode = (int)billingException.StatusCode;
            if (statusCode >= 500)
            {
                _logger.LogError(
                    billingException,
                    "Subscription billing failed with safe code {BillingErrorCode}.",
                    billingException.Code);
            }
            else
            {
                _logger.LogWarning(
                    "Subscription billing request was rejected with safe code {BillingErrorCode}.",
                    billingException.Code);
            }

            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/problem+json";
            var problem = new ProblemDetails
            {
                Status = statusCode,
                Title = "Subscription billing request failed",
                Detail = billingException.SafeMessage
            };
            problem.Extensions["code"] = billingException.Code;
            await context.Response.WriteAsJsonAsync(problem);
            return;
        }

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
}

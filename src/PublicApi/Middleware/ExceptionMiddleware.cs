using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.AspNetCore.Mvc;
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
        else if (exception is SubscriptionPlanNotFoundException)
        {
            await WriteProblemAsync(context, HttpStatusCode.NotFound, "Subscription plan not found", exception.Message);
        }
        else if (exception is ShopperIdentityNotFoundException)
        {
            await WriteProblemAsync(context, HttpStatusCode.Unauthorized, "Invalid shopper identity", exception.Message);
        }
        else if (exception is BillingProviderException providerException)
        {
            var (status, title) = providerException.Kind switch
            {
                BillingFailureKind.Rejected => (HttpStatusCode.UnprocessableEntity, "Billing request rejected"),
                BillingFailureKind.InvalidResponse => (HttpStatusCode.BadGateway, "Billing response invalid"),
                _ => (HttpStatusCode.ServiceUnavailable, "Billing service unavailable")
            };
            _logger.LogWarning(
                exception,
                "Maxio billing operation failed with kind {BillingFailureKind}",
                providerException.Kind);
            await WriteProblemAsync(context, status, title, providerException.Message);
        }
        else
        {
            _logger.LogError(exception, "Unhandled PublicApi exception");
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = "An unexpected error occurred."
            }.ToString());
        }
    }

    private static async Task WriteProblemAsync(
        HttpContext context,
        HttpStatusCode status,
        string title,
        string detail)
    {
        context.Response.StatusCode = (int)status;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = (int)status,
            Title = title,
            Detail = detail,
            Instance = context.Request.Path
        });
    }
}

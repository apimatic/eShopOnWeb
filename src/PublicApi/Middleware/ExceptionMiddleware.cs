using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using Microsoft.eShopWeb.PublicApi.Subscriptions.Maxio;
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

        switch (exception)
        {
            case DuplicateException duplicationException:
                context.Response.StatusCode = (int)HttpStatusCode.Conflict;
                await WriteAsync(context, duplicationException.Message);
                break;

            case SubscriptionPlanNotFoundException notFoundException:
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                await WriteAsync(context, notFoundException.Message);
                break;

            case BillingAccountNotFoundException accountNotFoundException:
                context.Response.StatusCode = (int)HttpStatusCode.UnprocessableEntity;
                await WriteAsync(context, accountNotFoundException.Message);
                break;

            case MaxioApiException maxioException:
                _logger.LogError(exception, "Maxio API request failed with status {StatusCode}.", maxioException.StatusCode);
                context.Response.StatusCode = (int)HttpStatusCode.BadGateway;
                await WriteAsync(context, "The billing provider could not complete the request. Please try again later.");
                break;

            default:
                _logger.LogError(exception, "An unhandled exception occurred while processing the request.");
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                await WriteAsync(context, "An error occurred while processing the request.");
                break;
        }
    }

    private static async Task WriteAsync(HttpContext context, string message)
    {
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }
}

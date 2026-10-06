using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;

namespace Microsoft.eShopWeb.PublicApi.Middleware;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;

    public ExceptionMiddleware(RequestDelegate next)
    {
        _next = next;
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
        else if (exception is SubscriptionPlanNotFoundException planNotFoundException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = planNotFoundException.Message
            }.ToString());
        }
        else if (exception is MaxioApiException maxioException)
        {
            // Surface Maxio-side failures (validation, not found, upstream outage) instead of masking them as 500s.
            context.Response.StatusCode = (int)MapMaxioStatus(maxioException.StatusCode);
            var errorResponse = new
            {
                StatusCode = context.Response.StatusCode,
                Message = "Maxio Advanced Billing request failed.",
                MaxioStatus = (int)maxioException.StatusCode,
                Errors = maxioException.Errors
            };
            await context.Response.WriteAsync(JsonSerializer.Serialize(errorResponse));
        }
        else
        {
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = exception.Message
            }.ToString());
        }
    }

    private static HttpStatusCode MapMaxioStatus(HttpStatusCode maxioStatus)
    {
        return maxioStatus switch
        {
            HttpStatusCode.NotFound => HttpStatusCode.NotFound,
            HttpStatusCode.UnprocessableEntity => HttpStatusCode.UnprocessableEntity,
            HttpStatusCode.BadRequest => HttpStatusCode.BadRequest,
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => HttpStatusCode.BadGateway,
            _ => HttpStatusCode.BadGateway
        };
    }
}

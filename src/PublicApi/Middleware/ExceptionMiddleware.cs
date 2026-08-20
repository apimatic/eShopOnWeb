using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

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
            await WriteError(context, HttpStatusCode.Conflict, duplicationException.Message);
        }
        else if (exception is UnknownSubscriptionPlanException unknownPlan)
        {
            await WriteError(context, HttpStatusCode.BadRequest, unknownPlan.Message);
        }
        else if (exception is MaxioConfigurationException configurationException)
        {
            await WriteError(context, HttpStatusCode.ServiceUnavailable, configurationException.Message);
        }
        else if (exception is MaxioApiException maxioException)
        {
            var status = maxioException.StatusCode is >= 400 and < 500
                ? HttpStatusCode.BadRequest
                : HttpStatusCode.BadGateway;
            await WriteError(context, status, maxioException.Message);
        }
        else
        {
            await WriteError(context, HttpStatusCode.InternalServerError, exception.Message);
        }
    }

    private static async Task WriteError(HttpContext context, HttpStatusCode statusCode, string message)
    {
        context.Response.StatusCode = (int)statusCode;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }
}

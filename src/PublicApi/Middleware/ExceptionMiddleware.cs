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

    private static Task WriteErrorAsync(HttpContext context, HttpStatusCode statusCode, string message)
    {
        context.Response.StatusCode = (int)statusCode;
        return context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";

        if (exception is DuplicateException duplicationException)
        {
            await WriteErrorAsync(context, HttpStatusCode.Conflict, duplicationException.Message);
        }
        else if (exception is SubscriptionPlanNotFoundException planNotFound)
        {
            await WriteErrorAsync(context, HttpStatusCode.BadRequest, planNotFound.Message);
        }
        else if (exception is MaxioConfigurationException configurationException)
        {
            await WriteErrorAsync(context, HttpStatusCode.ServiceUnavailable, configurationException.Message);
        }
        else if (exception is MaxioBillingException maxioException)
        {
            var status = maxioException.StatusCode is >= 400 and < 500
                ? (HttpStatusCode)maxioException.StatusCode
                : HttpStatusCode.BadGateway;
            await WriteErrorAsync(context, status, maxioException.Message);
        }
        else
        {
            await WriteErrorAsync(context, HttpStatusCode.InternalServerError, exception.Message);
        }
    }
}

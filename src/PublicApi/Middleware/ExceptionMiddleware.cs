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

    private static async Task WriteAsync(HttpContext context, HttpStatusCode statusCode, string message)
    {
        context.Response.StatusCode = (int)statusCode;
        await context.Response.WriteAsync(new ErrorDetails()
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
            await WriteAsync(context, HttpStatusCode.Conflict, duplicationException.Message);
        }
        else if (exception is SubscriptionPlanNotFoundException planNotFound)
        {
            await WriteAsync(context, HttpStatusCode.NotFound, planNotFound.Message);
        }
        else if (exception is BillingValidationException validation)
        {
            await WriteAsync(context, HttpStatusCode.BadRequest, validation.Message);
        }
        else if (exception is BillingProviderException provider)
        {
            var status = provider.StatusCode == 404
                ? HttpStatusCode.NotFound
                : HttpStatusCode.BadGateway;
            await WriteAsync(context, status, provider.Message);
        }
        else
        {
            await WriteAsync(context, HttpStatusCode.InternalServerError, exception.Message);
        }
    }
}

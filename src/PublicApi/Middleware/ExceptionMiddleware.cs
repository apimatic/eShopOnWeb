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
            await WriteErrorAsync(context, HttpStatusCode.Conflict, duplicationException.Message);
        }
        else if (exception is BillingValidationException validationException)
        {
            await WriteErrorAsync(context, HttpStatusCode.BadRequest, validationException.Message);
        }
        else if (exception is BillingNotConfiguredException notConfiguredException)
        {
            await WriteErrorAsync(context, HttpStatusCode.ServiceUnavailable, notConfiguredException.Message);
        }
        else if (exception is BillingGatewayException gatewayException)
        {
            var status = MapGatewayStatus(gatewayException.StatusCode);
            await WriteErrorAsync(context, status, gatewayException.Message);
        }
        else
        {
            await WriteErrorAsync(context, HttpStatusCode.InternalServerError, exception.Message);
        }
    }

    private static HttpStatusCode MapGatewayStatus(int statusCode)
    {
        if (statusCode == (int)HttpStatusCode.NotFound)
        {
            return HttpStatusCode.NotFound;
        }

        if (statusCode >= 400 && statusCode < 500
            && statusCode != (int)HttpStatusCode.Unauthorized
            && statusCode != (int)HttpStatusCode.Forbidden)
        {
            return HttpStatusCode.BadRequest;
        }

        return HttpStatusCode.BadGateway;
    }

    private static async Task WriteErrorAsync(HttpContext context, HttpStatusCode statusCode, string message)
    {
        context.Response.StatusCode = (int)statusCode;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }
}

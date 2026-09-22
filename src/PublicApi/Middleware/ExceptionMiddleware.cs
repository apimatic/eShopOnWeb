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
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = duplicationException.Message
            }.ToString());
        }
        else if (exception is BillingProviderException billingException)
        {
            // A caller-fixable rejection surfaces as its own 4xx (default 400); a provider/transport
            // fault surfaces as the mapped 5xx (default 502). Only the caller-safe message is returned.
            var status = billingException.IsCallerError
                ? (IsClientError(billingException.StatusCode) ? (int)billingException.StatusCode! : (int)HttpStatusCode.BadRequest)
                : (billingException.StatusCode is not null ? (int)billingException.StatusCode : (int)HttpStatusCode.BadGateway);

            context.Response.StatusCode = status;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = status,
                Message = billingException.Message
            }.ToString());
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

    private static bool IsClientError(HttpStatusCode? status)
        => status is not null && (int)status >= 400 && (int)status < 500;
}

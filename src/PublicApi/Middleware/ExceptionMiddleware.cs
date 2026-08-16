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

        var statusCode = exception switch
        {
            // A resource that does not exist, or is not the caller's, is reported as not-found.
            PaymentResourceNotFoundException => HttpStatusCode.NotFound,
            // A challenge requiring browser approval — we stop and surface it rather than proceeding.
            PaymentApprovalRequiredException => HttpStatusCode.Conflict,
            // A stale hold that can no longer be renewed — operator-actionable.
            ReauthorizationNotAllowedException => HttpStatusCode.Conflict,
            // Invalid state transitions / business-rule violations (e.g. refund exceeding capture).
            PaymentException => HttpStatusCode.Conflict,
            DuplicateException => HttpStatusCode.Conflict,
            _ => HttpStatusCode.InternalServerError,
        };

        context.Response.StatusCode = (int)statusCode;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = exception.Message
        }.ToString());
    }
}

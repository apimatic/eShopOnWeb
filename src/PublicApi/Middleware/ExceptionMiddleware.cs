using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.SquareIntegration;

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

        if (exception is SquareIntegrationException squareException)
        {
            // Square failures carry caller-safe messages; our own credential and quota problems are not the caller's.
            context.Response.StatusCode = squareException.Kind switch
            {
                SquareFailureKind.NotConnected => (int)HttpStatusCode.Conflict,
                SquareFailureKind.Conflict => (int)HttpStatusCode.Conflict,
                SquareFailureKind.RateLimited => (int)HttpStatusCode.ServiceUnavailable,
                SquareFailureKind.OutcomeUnknown => (int)HttpStatusCode.ServiceUnavailable,
                _ => (int)HttpStatusCode.BadGateway,
            };
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = squareException.Message
            }.ToString());
        }
        else if (exception is OperationCanceledException && !context.RequestAborted.IsCancellationRequested)
        {
            context.Response.StatusCode = (int)HttpStatusCode.GatewayTimeout;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = "The request did not complete in time."
            }.ToString());
        }
        else if (exception is DuplicateException duplicationException)
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
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = exception.Message
            }.ToString());
        }
    }
}

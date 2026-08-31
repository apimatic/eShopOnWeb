using System;
using System.Net;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
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
        else if (exception is NotFoundException notFoundException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = notFoundException.Message
            }.ToString());
        }
        else if (exception is PhoneNumberNotValidException phoneNumberNotValidException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = phoneNumberNotValidException.Message
            }.ToString());
        }
        else if (exception is DomainRuleViolationException domainRuleViolationException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = domainRuleViolationException.Message
            }.ToString());
        }
        else if (exception is MessagingProviderException messagingProviderException)
        {
            // Our credentials/quota failures (401/403/429) are not the caller's fault and
            // must not be passed through as if they were; other provider 4xx are handed
            // back at the same status so the caller can act on them.
            context.Response.StatusCode = messagingProviderException.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => (int)HttpStatusCode.BadGateway,
                HttpStatusCode.TooManyRequests => (int)HttpStatusCode.ServiceUnavailable,
                >= (HttpStatusCode)400 and < (HttpStatusCode)500 => (int)messagingProviderException.StatusCode,
                _ => (int)HttpStatusCode.BadGateway
            };
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = messagingProviderException.Message
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

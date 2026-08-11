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

        // Map domain/payment failures to actionable HTTP status codes. Order matters: the more
        // specific gateway subclasses must be checked before their PaymentGatewayException base.
        var (statusCode, message) = exception switch
        {
            DuplicateException => ((int)HttpStatusCode.Conflict, exception.Message),
            PaymentNotFoundException => ((int)HttpStatusCode.NotFound, exception.Message),
            UnauthorizedAccessException => ((int)HttpStatusCode.Unauthorized, exception.Message),
            PaymentChallengeRequiredException => ((int)HttpStatusCode.UnprocessableEntity, exception.Message),
            AuthorizationNotRenewableException => ((int)HttpStatusCode.Conflict, exception.Message),
            PaymentGatewayException => ((int)HttpStatusCode.BadGateway, exception.Message),
            PaymentException => ((int)HttpStatusCode.BadRequest, exception.Message),
            _ => ((int)HttpStatusCode.InternalServerError, exception.Message)
        };

        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = statusCode,
            Message = message
        }.ToString());
    }
}

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

        var (statusCode, message) = exception switch
        {
            DuplicateException => ((int)HttpStatusCode.Conflict, exception.Message),
            OrderNotFoundException => ((int)HttpStatusCode.NotFound, exception.Message),
            PaymentValidationException => ((int)HttpStatusCode.BadRequest, exception.Message),
            AuthorizationNotRenewableException => ((int)HttpStatusCode.Conflict, exception.Message),
            PaymentChallengeRequiredException => ((int)HttpStatusCode.UnprocessableEntity, exception.Message),
            // Surface a provider 4xx as a client 4xx; a transport/unknown failure as a 502.
            PaymentGatewayException gatewayException => (
                gatewayException.ProviderStatusCode is int code && code is >= 400 and < 500
                    ? code
                    : (int)HttpStatusCode.BadGateway,
                gatewayException.Message),
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

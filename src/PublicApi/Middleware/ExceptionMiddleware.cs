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

        // Map domain/payment exceptions to meaningful status codes. Order matters: more specific
        // (derived) types are listed before their base type.
        var statusCode = exception switch
        {
            EntityNotFoundException => HttpStatusCode.NotFound,
            PaymentValidationException => HttpStatusCode.BadRequest,
            DuplicateException => HttpStatusCode.Conflict,
            OrderPaymentException => HttpStatusCode.Conflict,
            AuthorizationNotRenewableException => HttpStatusCode.Conflict,
            PaymentChallengeRequiredException => HttpStatusCode.UnprocessableEntity,
            PayPalGatewayException => HttpStatusCode.BadGateway,
            _ => HttpStatusCode.InternalServerError
        };

        context.Response.StatusCode = (int)statusCode;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = exception.Message
        }.ToString());
    }
}

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

        var (status, message) = exception switch
        {
            DuplicateException duplicationException => ((int)HttpStatusCode.Conflict, duplicationException.Message),
            PaymentValidationException validation => ((int)HttpStatusCode.BadRequest, validation.Message),
            PaymentNotFoundException notFound => ((int)HttpStatusCode.NotFound, notFound.Message),
            PaymentForbiddenException forbidden => ((int)HttpStatusCode.Forbidden, forbidden.Message),
            PaymentConflictException conflict => ((int)HttpStatusCode.Conflict, conflict.Message),
            AuthorizationUnrenewableException unrenewable => ((int)HttpStatusCode.Conflict, unrenewable.Message),
            PayerActionRequiredException payerAction => ((int)HttpStatusCode.Conflict, payerAction.Message),
            UnauthorizedAccessException unauthorized => ((int)HttpStatusCode.Unauthorized, unauthorized.Message),
            PayPalGatewayException gateway => (MapGatewayStatus(gateway.StatusCode), gateway.Message),
            _ => ((int)HttpStatusCode.InternalServerError, exception.Message)
        };

        context.Response.StatusCode = status;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = status,
            Message = message
        }.ToString());
    }

    private static int MapGatewayStatus(int paypalStatus) => paypalStatus switch
    {
        400 or 401 or 403 or 404 or 409 or 422 => paypalStatus,
        _ => (int)HttpStatusCode.BadGateway
    };
}

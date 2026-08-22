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
            DuplicateException ex => (HttpStatusCode.Conflict, ex.Message),
            EntityNotFoundException ex => (HttpStatusCode.NotFound, ex.Message),
            ForbiddenAccessException ex => (HttpStatusCode.Forbidden, ex.Message),
            InvalidOrderStateException ex => (HttpStatusCode.Conflict, ex.Message),
            PaymentValidationException ex => (HttpStatusCode.BadRequest, ex.Message),
            PayerActionRequiredException ex => (HttpStatusCode.Conflict, ex.Message),
            AuthorizationCannotBeRenewedException ex => (HttpStatusCode.Conflict, ex.Message),
            PayPalGatewayException ex when ex.HttpStatus is 400 or 422 => (HttpStatusCode.BadRequest, ex.Message),
            PayPalGatewayException ex => (HttpStatusCode.BadGateway, ex.Message),
            UnauthorizedAccessException ex => (HttpStatusCode.Unauthorized, ex.Message),
            _ => (HttpStatusCode.InternalServerError, exception.Message)
        };

        context.Response.StatusCode = (int)status;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }
}

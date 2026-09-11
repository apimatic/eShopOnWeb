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
        context.Response.StatusCode = (int)MapStatusCode(exception);

        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = exception.Message
        }.ToString());
    }

    private static HttpStatusCode MapStatusCode(Exception exception) => exception switch
    {
        DuplicateException => HttpStatusCode.Conflict,
        NotFoundException => HttpStatusCode.NotFound,
        // A shopper facing a card challenge, or a business rule that blocks the operation.
        PaymentException => HttpStatusCode.Conflict,
        // Bad caller input (unknown catalog item, missing idempotency key, invalid quantity, etc.).
        ArgumentException => HttpStatusCode.BadRequest,
        InvalidOperationException => HttpStatusCode.Conflict,
        _ => HttpStatusCode.InternalServerError
    };
}

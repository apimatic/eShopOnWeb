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

        var (statusCode, message) = Map(exception);
        context.Response.StatusCode = statusCode;

        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = statusCode,
            Message = message
        }.ToString());
    }

    private static (int StatusCode, string Message) Map(Exception exception) => exception switch
    {
        DuplicateException => ((int)HttpStatusCode.Conflict, exception.Message),

        OrderNotFoundException => ((int)HttpStatusCode.NotFound, exception.Message),
        PaymentMethodNotFoundException => ((int)HttpStatusCode.NotFound, exception.Message),

        // A browser approval challenge is deliberately unsupported — surface it as an actionable gap.
        PaymentChallengeRequiredException => ((int)HttpStatusCode.UnprocessableEntity, exception.Message),

        // Business-rule / state violations the caller can act on.
        PaymentException => ((int)HttpStatusCode.BadRequest, exception.Message),

        // PayPal itself returned an error we could not turn into a domain outcome.
        PayPalApiException => ((int)HttpStatusCode.BadGateway, exception.Message),

        _ => ((int)HttpStatusCode.InternalServerError, exception.Message)
    };
}

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

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";

        var statusCode = exception switch
        {
            // The resource does not exist, or is not the caller's (kept indistinguishable).
            OrderNotFoundException => HttpStatusCode.NotFound,
            SavedCardNotFoundException => HttpStatusCode.NotFound,

            // Bad input.
            OrderMustHaveItemsException => HttpStatusCode.BadRequest,
            CatalogItemNotFoundException => HttpStatusCode.BadRequest,
            PaymentValidationException => HttpStatusCode.BadRequest,

            // Shopper must approve in a browser — this app deliberately does not build that round-trip.
            PayPalChallengeException => HttpStatusCode.PaymentRequired,

            // Payment state conflicts / a non-renewable authorization (operator-actionable).
            AuthorizationUnusableException => HttpStatusCode.Conflict,
            DuplicateException => HttpStatusCode.Conflict,
            InvalidOperationException => HttpStatusCode.Conflict,

            // Upstream PayPal failure.
            PayPalException => HttpStatusCode.BadGateway,

            _ => HttpStatusCode.InternalServerError
        };

        context.Response.StatusCode = (int)statusCode;

        // Never leak internal detail on an unexpected error.
        var message = statusCode == HttpStatusCode.InternalServerError
            ? "An unexpected error occurred."
            : exception.Message;

        await context.Response.WriteAsync(new ErrorDetails
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }
}

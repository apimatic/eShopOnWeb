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

        var (statusCode, message) = MapException(exception);
        context.Response.StatusCode = (int)statusCode;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }

    private static (HttpStatusCode statusCode, string message) MapException(Exception exception)
    {
        switch (exception)
        {
            case DuplicateException:
            case InvalidInvoiceOperationException:
                // A conflicting request against the current state of the resource.
                return (HttpStatusCode.Conflict, exception.Message);

            case InvoiceNotFoundException:
            case OrderNotFoundException:
                return (HttpStatusCode.NotFound, exception.Message);

            case InvoiceProviderException providerException:
                // Translate a provider-side refusal into the caller's terms: a state refusal (4xx) is a
                // conflict for the caller, a missing resource stays a 404, and a genuine provider fault
                // surfaces as a bad gateway.
                var status = providerException.ProviderStatusCode switch
                {
                    404 => HttpStatusCode.NotFound,
                    >= 400 and < 500 => HttpStatusCode.Conflict,
                    _ => HttpStatusCode.BadGateway
                };
                return (status, providerException.Message);

            case ArgumentException:
                return (HttpStatusCode.BadRequest, exception.Message);

            default:
                return (HttpStatusCode.InternalServerError, exception.Message);
        }
    }
}

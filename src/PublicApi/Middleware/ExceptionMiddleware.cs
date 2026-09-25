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

    // One place converts domain/payment failures into a caller-facing status. A provider 4xx the
    // caller can act on passes through; our-credentials/quota and transport failures become 5xx; no
    // internal/SDK detail is leaked.
    private static (int StatusCode, string Message) Map(Exception exception)
    {
        switch (exception)
        {
            case DuplicateException:
                return ((int)HttpStatusCode.Conflict, exception.Message);

            case OrderNotFoundException:
                return ((int)HttpStatusCode.NotFound, exception.Message);

            case ArgumentException:
                return ((int)HttpStatusCode.BadRequest, exception.Message);

            case PayerActionRequiredException:
                return ((int)HttpStatusCode.Conflict, exception.Message);

            case PaymentException payment:
                var status = (int?)payment.StatusCode;
                // Our credentials / quota — the caller did nothing wrong and cannot fix it.
                if (status is 401 or 403 or 429)
                    return ((int)HttpStatusCode.BadGateway, "Payment provider unavailable.");
                // The provider rejected the caller's request — hand back the same status.
                if (status is >= 400 and < 500)
                    return (status!.Value, payment.Message);
                // Transport, timeout, provider 5xx — no meaningful caller status.
                return ((int)HttpStatusCode.BadGateway, payment.Message);

            default:
                return ((int)HttpStatusCode.InternalServerError, exception.Message);
        }
    }
}

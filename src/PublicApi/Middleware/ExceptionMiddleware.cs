using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;

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

        var (status, message) = Map(exception);
        context.Response.StatusCode = (int)status;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }

    private static (HttpStatusCode status, string message) Map(Exception exception)
    {
        switch (exception)
        {
            case DuplicateException:
                return (HttpStatusCode.Conflict, exception.Message);
            case PaymentResourceNotFoundException:
                return (HttpStatusCode.NotFound, exception.Message);
            case PaymentValidationException:
                return (HttpStatusCode.BadRequest, exception.Message);
            case PaymentConflictException:
                return (HttpStatusCode.Conflict, exception.Message);
            case PaymentGatewayException gw:
                var status = gw.Kind switch
                {
                    PaymentFailureKind.Rejected => HttpStatusCode.BadRequest,
                    PaymentFailureKind.Conflict => HttpStatusCode.Conflict,
                    _ => HttpStatusCode.BadGateway
                };
                // Operator-facing detail (PayPal issue codes / debug id) is safe to surface and helps action.
                var message = string.IsNullOrWhiteSpace(gw.OperatorDetail)
                    ? gw.Message : $"{gw.Message} [{gw.OperatorDetail}]";
                return (status, message);
            default:
                return (HttpStatusCode.InternalServerError, exception.Message);
        }
    }
}

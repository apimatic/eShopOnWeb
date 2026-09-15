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

    private static (int statusCode, string message) Map(Exception exception)
    {
        switch (exception)
        {
            case DuplicateException:
                return ((int)HttpStatusCode.Conflict, exception.Message);
            case ResourceNotFoundException:
                return ((int)HttpStatusCode.NotFound, exception.Message);
            case PaymentValidationException:
                return ((int)HttpStatusCode.BadRequest, exception.Message);
            case InvalidPaymentOperationException:
                return ((int)HttpStatusCode.Conflict, exception.Message);
            case PaymentApprovalRequiredException approval:
                var message = approval.ApprovalUrl is null
                    ? approval.Message
                    : $"{approval.Message} Approval URL: {approval.ApprovalUrl}";
                return ((int)HttpStatusCode.Conflict, message);
            case PayPalApiException payPal:
                // A deterministic provider rejection (4xx) the caller can act on surfaces as that same
                // client status; a transport/unknown failure surfaces as 502.
                var status = payPal.IsClientError && payPal.ProviderStatusCode is >= 400 and < 500
                    ? payPal.ProviderStatusCode.Value
                    : (int)HttpStatusCode.BadGateway;
                return (status, payPal.Message);
            default:
                return ((int)HttpStatusCode.InternalServerError, exception.Message);
        }
    }
}

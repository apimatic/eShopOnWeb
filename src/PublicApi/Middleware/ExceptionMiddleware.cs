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
        DuplicateException e => ((int)HttpStatusCode.Conflict, e.Message),
        NotFoundException e => ((int)HttpStatusCode.NotFound, e.Message),
        PaymentChallengeRequiredException e => ((int)HttpStatusCode.UnprocessableEntity, e.Message),
        // A caller-fixable rejection (bad card, invalid amount) is 422; our-credentials / provider /
        // transport failures the caller cannot fix are 502.
        PaymentGatewayException e => (e.IsClientError
            ? (int)HttpStatusCode.UnprocessableEntity
            : (int)HttpStatusCode.BadGateway, e.Message),
        // Illegal state transition (e.g. fulfilling an order that was never authorized).
        InvalidOperationException e => ((int)HttpStatusCode.Conflict, e.Message),
        ArgumentException e => ((int)HttpStatusCode.BadRequest, e.Message),
        _ => ((int)HttpStatusCode.InternalServerError, exception.Message)
    };
}

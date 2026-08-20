using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest>
{
    private readonly IOrderNotificationService _notifications;

    public ResendNotificationEndpoint(IOrderNotificationService notifications)
    {
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, HttpContext httpContext) =>
            {
                request.NotificationId = notificationId;
                request.CancellationToken = httpContext.RequestAborted;
                return await HandleAsync(request);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRequest request)
    {
        try
        {
            var sent = await _notifications.ResendAsync(request.NotificationId, request.IdempotencyKey, request.CancellationToken);
            return Results.Ok(new ResendNotificationResponse
            {
                NotificationId = sent.Id,
                Status = sent.Status,
                ProviderSid = sent.ProviderSid
            });
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Results.Conflict(new { message = ex.Message });
        }
        catch (OrderMessagingException)
        {
            return Results.Json(new { message = "The messaging provider is unavailable." }, statusCode: StatusCodes.Status502BadGateway);
        }
    }
}

public class ResendNotificationRequest : BaseRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;
    internal int NotificationId { get; set; }
    internal CancellationToken CancellationToken { get; set; }
}

public class ResendNotificationResponse : BaseResponse
{
    public int NotificationId { get; set; }
    public string? Status { get; set; }
    public string? ProviderSid { get; set; }
}

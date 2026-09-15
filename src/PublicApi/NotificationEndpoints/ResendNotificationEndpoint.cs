using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRouteRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest body, IOrderNotificationService service, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(new ResendNotificationRouteRequest(notificationId, body?.IdempotencyKey ?? string.Empty), service, cancellationToken);
            })
            .Produces<ResendNotificationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(ResendNotificationRouteRequest request, IOrderNotificationService service)
        => HandleAsync(request, service, CancellationToken.None);

    private async Task<IResult> HandleAsync(
        ResendNotificationRouteRequest request,
        IOrderNotificationService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var notification = await service.ResendAsync(request.NotificationId, request.IdempotencyKey, cancellationToken);
            return Results.Ok(new ResendNotificationResponse
            {
                NotificationId = notification.Id,
                Status = notification.Status,
                ProviderSid = notification.ProviderSid
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
    }
}

public class ResendNotificationRouteRequest : BaseRequest
{
    public ResendNotificationRouteRequest(int notificationId, string idempotencyKey)
    {
        NotificationId = notificationId;
        IdempotencyKey = idempotencyKey;
    }

    public int NotificationId { get; }
    public string IdempotencyKey { get; }
}

public class ResendNotificationResponse
{
    public int NotificationId { get; set; }
    public string? Status { get; set; }
    public string? ProviderSid { get; set; }
}

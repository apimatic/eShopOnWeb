using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: re-sends a message that did not reach the shopper. The caller supplies
/// an idempotency key (via the <c>Idempotency-Key</c> header or an <c>idempotencyKey</c>
/// query value): repeating a request under the same key does not send a second message,
/// while a genuine second attempt under a fresh key does.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, INotificationOperationsService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId,
             [FromHeader(Name = "Idempotency-Key")] string? idempotencyHeader,
             string? idempotencyKey,
             INotificationOperationsService service) =>
            {
                return await HandleAsync(
                    new ResendNotificationRequest
                    {
                        NotificationId = notificationId,
                        IdempotencyKey = idempotencyHeader ?? idempotencyKey
                    },
                    service);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRequest request, INotificationOperationsService service)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            return Results.BadRequest("An idempotency key is required (Idempotency-Key header or idempotencyKey query value).");

        var notification = await service.ResendAsync(request.NotificationId, request.IdempotencyKey!);
        if (notification is null)
            return Results.NotFound();

        return Results.Ok(new ResendNotificationResponse(request.CorrelationId())
        {
            NotificationId = notification.Id,
            Status = notification.Status,
            ProviderMessageSid = notification.ProviderMessageSid
        });
    }
}

public class ResendNotificationRequest : BaseRequest
{
    public int NotificationId { get; set; }
    public string? IdempotencyKey { get; set; }
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) { }
    public ResendNotificationResponse() { }

    /// <summary>Identifier of the message the resend produced.</summary>
    public int NotificationId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
}

using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SmsNotifications.NotificationEndpoints;

public class ResendNotificationRequest : BaseRequest
{
    [JsonIgnore]
    public int NotificationId { get; set; }

    /// <summary>Caller-supplied key that makes the re-send idempotent. May also be supplied via the Idempotency-Key header.</summary>
    public string? IdempotencyKey { get; set; }
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(System.Guid correlationId) : base(correlationId) { }
    public ResendNotificationResponse() { }

    /// <summary>The identifier of the message the resend produced (the same one on an idempotent replay).</summary>
    public int NotificationId { get; set; }

    /// <summary>"sent" for a fresh send, "duplicate" when the key had already been used.</summary>
    public string Outcome { get; set; } = string.Empty;

    public string ProviderStatus { get; set; } = string.Empty;
}

/// <summary>
/// POST /api/notifications/{notificationId}/resend &ndash; an operator re-sends a message that did not
/// reach the shopper. Idempotent on the caller's key: a repeat under the same key sends nothing more.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest? request, HttpRequest httpRequest, IOrderNotificationService notificationService) =>
            {
                var key = request?.IdempotencyKey;
                if (string.IsNullOrWhiteSpace(key) && httpRequest.Headers.TryGetValue("Idempotency-Key", out var header))
                {
                    key = header.ToString();
                }
                if (string.IsNullOrWhiteSpace(key))
                {
                    return Results.BadRequest(new { message = "An idempotency key is required (request body 'idempotencyKey' or the Idempotency-Key header)." });
                }

                return await HandleAsync(new ResendNotificationRequest { NotificationId = notificationId, IdempotencyKey = key }, notificationService);
            })
            .Produces<ResendNotificationResponse>()
            .Produces<ResendNotificationResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRequest request, IOrderNotificationService notificationService)
    {
        var result = await notificationService.ResendAsync(request.NotificationId, request.IdempotencyKey!);

        switch (result.Status)
        {
            case ResendStatus.OriginalNotFound:
                return Results.NotFound();

            case ResendStatus.AlreadyDelivered:
                return Results.Conflict(new { message = "The message already reached the shopper; there is nothing to re-send." });

            case ResendStatus.Duplicate:
                return Results.Ok(new ResendNotificationResponse(request.CorrelationId())
                {
                    NotificationId = result.Notification!.Id,
                    Outcome = "duplicate",
                    ProviderStatus = result.Notification.ProviderStatus
                });

            default: // Sent
                var response = new ResendNotificationResponse(request.CorrelationId())
                {
                    NotificationId = result.Notification!.Id,
                    Outcome = "sent",
                    ProviderStatus = result.Notification.ProviderStatus
                };
                return Results.Created($"api/notifications/{result.Notification.Id}", response);
        }
    }
}

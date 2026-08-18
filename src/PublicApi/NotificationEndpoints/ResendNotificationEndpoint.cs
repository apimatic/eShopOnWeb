using System;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: re-send a message that did not reach the shopper. Repeating a request under the
/// same idempotency key returns the message the first attempt produced without sending a second one.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId:int}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, IOrderNotificationService service, CancellationToken ct) =>
            {
                request.NotificationId = notificationId;
                return await HandleAsync(request, service, ct);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(ResendNotificationRequest request, IOrderNotificationService service)
        => HandleAsync(request, service, default);

    public async Task<IResult> HandleAsync(ResendNotificationRequest request, IOrderNotificationService service, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { message = "An idempotency key is required." });
        }

        var result = await service.ResendAsync(request.NotificationId, request.IdempotencyKey, ct);
        if (result.Outcome == ResendOutcome.NotFound || result.Notification is null)
        {
            return Results.NotFound();
        }

        var response = new ResendNotificationResponse(request.CorrelationId())
        {
            NotificationId = result.Notification.Id,
            DeliveryStatus = result.Notification.DeliveryStatus,
            Duplicate = result.Outcome == ResendOutcome.Duplicate
        };
        return Results.Ok(response);
    }
}

public class ResendNotificationRequest : BaseRequest
{
    /// <summary>Caller-supplied idempotency key: repeating a request under the same key sends nothing new.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    [JsonIgnore]
    public int NotificationId { get; set; }
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) { }

    public ResendNotificationResponse() { }

    /// <summary>The identifier of the message the resend produced.</summary>
    public int NotificationId { get; set; }
    public string DeliveryStatus { get; set; } = string.Empty;

    /// <summary>True when this responds to a repeat under an already-seen idempotency key.</summary>
    public bool Duplicate { get; set; }
}

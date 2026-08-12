using System;
using System.Text.Json.Serialization;
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

public class ResendNotificationRequest : BaseRequest
{
    /// <summary>
    /// Caller-supplied idempotency key. Repeating a request under the same key sends nothing new;
    /// a fresh key is a genuine new attempt. May also be supplied via the <c>Idempotency-Key</c> header.
    /// </summary>
    public string? IdempotencyKey { get; set; }

    [JsonIgnore]
    public int NotificationId { get; set; }
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) { }
    public ResendNotificationResponse() { }

    /// <summary>Identifier of the message the resend produced (or the original under a repeated key).</summary>
    public int NotificationId { get; set; }

    /// <summary>True when the key matched an earlier request and no new message was sent.</summary>
    public bool IdempotentReplay { get; set; }
}

/// <summary>
/// Re-sends a message that did not reach the shopper (operator action), idempotent on a caller-supplied key.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKeyHeader, ResendNotificationRequest? request, IOrderNotificationService service) =>
            {
                request ??= new ResendNotificationRequest();
                request.NotificationId = notificationId;
                if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
                    request.IdempotencyKey = idempotencyKeyHeader;

                if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
                    return Results.BadRequest(new { error = "An idempotency key is required (body 'idempotencyKey' or 'Idempotency-Key' header)." });

                return await HandleAsync(request, service);
            })
            .Produces<ResendNotificationResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRequest request, IOrderNotificationService service)
    {
        var result = await service.ResendAsync(request.NotificationId, request.IdempotencyKey!);
        if (!result.Found)
            return Results.NotFound();

        return Results.Ok(new ResendNotificationResponse(request.CorrelationId())
        {
            NotificationId = result.NotificationId,
            IdempotentReplay = result.WasIdempotentReplay
        });
    }
}

using System;
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
/// Operator action: re-sends a message that did not reach the shopper. The caller supplies an
/// idempotency key; repeating a request under the same key does not send a second message, while a
/// genuine second attempt under a fresh key does. Restricted to administrators.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, IOrderNotificationService>
{
    /// <summary>Header the idempotency key may also be supplied through.</summary>
    private const string IdempotencyHeader = "Idempotency-Key";

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId:int}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest? request, HttpContext http, IOrderNotificationService service, CancellationToken ct) =>
            {
                // The idempotency key may arrive in the body or in the Idempotency-Key header.
                var idempotencyKey = request?.IdempotencyKey;
                if (string.IsNullOrWhiteSpace(idempotencyKey) && http.Request.Headers.TryGetValue(IdempotencyHeader, out var header))
                {
                    idempotencyKey = header.ToString();
                }

                if (string.IsNullOrWhiteSpace(idempotencyKey))
                {
                    return Results.BadRequest(new { error = "An idempotency key is required (body 'idempotencyKey' or 'Idempotency-Key' header)." });
                }

                var result = await service.ResendAsync(notificationId, idempotencyKey.Trim(), ct);
                if (result == null)
                {
                    return Results.NotFound();
                }

                var response = new ResendNotificationResponse(request?.CorrelationId() ?? Guid.NewGuid())
                {
                    NotificationId = result.Notification.Id,
                    Duplicate = result.WasDuplicate,
                    Status = result.Notification.Status.ToString()
                };
                return Results.Ok(response);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(ResendNotificationRequest request, IOrderNotificationService service) =>
        Task.FromResult(Results.Ok());
}

public class ResendNotificationRequest : BaseRequest
{
    /// <summary>Caller-supplied idempotency key that makes a repeat under the same key a no-op.</summary>
    public string? IdempotencyKey { get; set; }
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) { }

    public ResendNotificationResponse() { }

    /// <summary>The identifier of the message the resend produced (or the first one, if a duplicate).</summary>
    public int NotificationId { get; set; }

    /// <summary>True when the idempotency key had already been used and no new message was sent.</summary>
    public bool Duplicate { get; set; }

    public string Status { get; set; } = string.Empty;
}

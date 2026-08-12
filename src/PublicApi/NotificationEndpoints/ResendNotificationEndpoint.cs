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
/// POST /api/notifications/{notificationId}/resend — operator re-sends a message that did not reach
/// the shopper. Carries a caller-supplied idempotency key: a repeat under the same key does not send
/// again; a fresh key is a genuine new send. Returns the produced message's id as top-level
/// <c>notificationId</c>. Operator-only.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, int, ResendNotificationRequest, HttpContext>
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";

    private readonly IOrderNotificationService _orderNotifications;

    public ResendNotificationEndpoint(IOrderNotificationService orderNotifications)
    {
        _orderNotifications = orderNotifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId:int}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest? request, HttpContext http) =>
            {
                return await HandleAsync(notificationId, request ?? new ResendNotificationRequest(), http);
            })
            .Produces<ResendNotificationResponse>(StatusCodes.Status201Created)
            .Produces<ResendNotificationResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, ResendNotificationRequest request, HttpContext http)
    {
        var idempotencyKey = request.IdempotencyKey;
        if (string.IsNullOrWhiteSpace(idempotencyKey) && http.Request.Headers.TryGetValue(IdempotencyKeyHeader, out var header))
        {
            idempotencyKey = header.ToString();
        }
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Results.BadRequest(new { error = $"An idempotency key is required (body 'idempotencyKey' or '{IdempotencyKeyHeader}' header)." });
        }

        var result = await _orderNotifications.ResendAsync(notificationId, idempotencyKey.Trim(), http.RequestAborted);
        if (result.NotFound)
        {
            return Results.NotFound();
        }

        var response = new ResendNotificationResponse
        {
            NotificationId = result.Notification!.Id,
            Replayed = result.AlreadyProcessed
        };

        // A replay returns the prior result (200); a genuine send returns the new resource (201).
        return result.AlreadyProcessed
            ? Results.Ok(response)
            : Results.Created($"api/notifications/{response.NotificationId}", response);
    }
}

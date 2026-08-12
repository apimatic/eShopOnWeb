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
/// Operator action: re-sends a message that did not reach the shopper. The caller supplies an
/// idempotency key (request body <c>idempotencyKey</c> or the <c>Idempotency-Key</c> header):
/// repeating a request under the same key does not send a second message, while a genuine
/// second attempt under a fresh key sends again.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int notificationId,
                ResendNotificationRequest? request,
                [FromHeader(Name = "Idempotency-Key")] string? idempotencyKeyHeader,
                IOrderNotificationService notifications) =>
            {
                var key = request?.IdempotencyKey;
                if (string.IsNullOrWhiteSpace(key))
                {
                    key = idempotencyKeyHeader;
                }
                if (string.IsNullOrWhiteSpace(key))
                {
                    return Results.BadRequest(new { message = "An idempotency key is required (body 'idempotencyKey' or 'Idempotency-Key' header)." });
                }

                var produced = await notifications.ResendAsync(notificationId, key!);
                if (produced is null)
                {
                    return Results.NotFound();
                }

                return Results.Ok(new ResendNotificationResponse
                {
                    NotificationId = produced.Id,
                    DeliveryStatus = produced.DeliveryStatus,
                    ProviderMessageSid = produced.ProviderMessageSid
                });
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }
}

public class ResendNotificationRequest
{
    public string? IdempotencyKey { get; set; }
}

public class ResendNotificationResponse
{
    /// <summary>Identifier of the message the resend produced (top-level).</summary>
    public int NotificationId { get; set; }
    public string DeliveryStatus { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
}

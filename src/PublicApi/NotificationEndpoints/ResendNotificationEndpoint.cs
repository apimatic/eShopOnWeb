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

public class ResendNotificationRequest
{
    /// <summary>Caller-supplied idempotency key: a repeat under the same key never sends twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationResponse
{
    /// <summary>The identifier of the message this resend produced (a new one, or the one a prior identical request produced).</summary>
    public int NotificationId { get; set; }
    public string DeliveryStatus { get; set; } = string.Empty;
}

/// <summary>
/// Operator action: re-sends a message that did not reach the shopper. Idempotent on the supplied key.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, int, ResendNotificationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, IOrderNotificationService service) =>
                await HandleAsync(notificationId, request, service))
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, ResendNotificationRequest request, IOrderNotificationService service)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { message = "An idempotency key is required." });
        }

        try
        {
            var notification = await service.ResendAsync(notificationId, request.IdempotencyKey);
            if (notification is null)
            {
                return Results.NotFound();
            }

            return Results.Ok(new ResendNotificationResponse
            {
                NotificationId = notification.Id,
                DeliveryStatus = notification.DeliveryStatus
            });
        }
        catch (NotificationContentUnavailableException ex)
        {
            return Results.Conflict(new { message = ex.Message });
        }
    }
}

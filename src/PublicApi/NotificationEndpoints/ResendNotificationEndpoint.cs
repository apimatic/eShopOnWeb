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
/// Operator action: re-sends a message that did not reach the shopper. The caller-supplied idempotency
/// key makes a repeat harmless — the same key returns the message already produced and sends nothing
/// more, while a fresh key is a genuine new attempt. Administrators only.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, int, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, IOrderNotificationService service, CancellationToken ct) =>
            {
                if (request is null || string.IsNullOrWhiteSpace(request.IdempotencyKey))
                    return Results.BadRequest(new { message = "An idempotency key is required." });

                var produced = await service.ResendAsync(notificationId, request.IdempotencyKey, ct);
                if (produced is null)
                    return Results.NotFound();

                var response = new ResendNotificationResponse
                {
                    NotificationId = produced.Id,
                    OrderId = produced.OrderId,
                    DeliveryStatus = produced.ProviderStatus,
                    MessageSid = produced.ProviderMessageSid
                };
                return Results.Ok(response);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(int notificationId, IOrderNotificationService service)
        => Task.FromResult<IResult>(Results.Empty);
}

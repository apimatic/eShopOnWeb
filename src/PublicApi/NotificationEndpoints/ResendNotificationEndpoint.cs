using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public record ResendNotificationBody(string IdempotencyKey);
public record ResendNotificationRequest(int NotificationId, string IdempotencyKey);
public record ResendNotificationResponse(int NotificationId, bool Deduplicated, string? ProviderMessageSid, string Outcome);

/// <summary>Operator action: re-send a message that did not reach the shopper. Repeating the request under the
/// same idempotency key returns the first result without sending again; a fresh key is a legitimate new send.
/// Returns the notificationId of the message the resend produced.</summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationBody body, IOrderNotificationService service) =>
            {
                return await HandleAsync(new ResendNotificationRequest(notificationId, body?.IdempotencyKey ?? string.Empty), service);
            })
            .Produces<ResendNotificationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRequest request, IOrderNotificationService service)
    {
        var result = await service.ResendAsync(request.NotificationId, request.IdempotencyKey, default);
        return Results.Ok(new ResendNotificationResponse(
            result.NotificationId, result.Deduplicated, result.ProviderMessageSid, result.Outcome.ToString()));
    }
}

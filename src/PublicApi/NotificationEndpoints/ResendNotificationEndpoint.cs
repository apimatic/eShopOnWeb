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
/// POST /api/notifications/{notificationId}/resend — an operator re-sends a message that did not reach the
/// shopper. The caller-supplied idempotency key makes a repeat under the same key a no-op while a fresh key sends
/// again. Returns the identifier of the message the resend produced. Operator action: administrator role only.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, int, ResendNotificationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, IOrderNotificationService notifier) =>
                await HandleAsync(notificationId, request, notifier))
            .Produces<ResendNotificationResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, ResendNotificationRequest request, IOrderNotificationService notifier)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.IdempotencyKey))
            return Results.BadRequest(new { message = "An idempotency key is required." });

        // NotificationNotFoundException (-> 404) and NotificationContentUnavailableException (-> 409) are mapped
        // by the exception middleware.
        var outcome = await notifier.ResendAsync(notificationId, request.IdempotencyKey);

        return Results.Ok(new ResendNotificationResponse
        {
            NotificationId = outcome.ResultNotificationId,
            AlreadyProcessed = outcome.AlreadyProcessed
        });
    }
}

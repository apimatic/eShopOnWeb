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
/// Operator action: re-sends a message that did not reach the shopper. A repeat under the same
/// idempotency key sends nothing and returns the message the first request produced; a fresh key
/// sends anew. The response returns the identifier of the message the resend produced.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, ResendRequest, INotificationManagementService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendRequest request, INotificationManagementService service) =>
            {
                request.NotificationId = notificationId;
                return await HandleAsync(request, service);
            })
            .Produces<ResendResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendRequest request, INotificationManagementService service)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { message = "An idempotency key is required to resend." });
        }

        var result = await service.ResendAsync(request.NotificationId, request.IdempotencyKey);
        switch (result.Outcome)
        {
            case ResendOutcome.NotFound:
                return Results.NotFound();
            case ResendOutcome.ReplayedIdempotent:
                return Results.Ok(new ResendResponse(request.CorrelationId())
                {
                    NotificationId = result.Notification!.Id,
                    Outcome = "replayed"
                });
            default:
                var response = new ResendResponse(request.CorrelationId())
                {
                    NotificationId = result.Notification!.Id,
                    Outcome = "created"
                };
                return Results.Created($"api/notifications/{response.NotificationId}", response);
        }
    }
}

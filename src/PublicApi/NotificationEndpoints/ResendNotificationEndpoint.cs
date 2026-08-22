using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRouteRequest, IOrderNotificationQueryService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int notificationId, ResendNotificationRequest request, IOrderNotificationQueryService service) =>
            {
                return await HandleAsync(new ResendNotificationRouteRequest(notificationId, request), service);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRouteRequest request, IOrderNotificationQueryService service)
    {
        if (string.IsNullOrWhiteSpace(request.Body.IdempotencyKey))
        {
            return Results.BadRequest(new { message = "IdempotencyKey is required." });
        }

        var created = await service.ResendAsync(request.NotificationId, request.Body.IdempotencyKey);
        return Results.Ok(new ResendNotificationResponse
        {
            NotificationId = created.Id,
            OriginalNotificationId = request.NotificationId,
            ProviderMessageSid = created.ProviderMessageSid,
            DeliveryStatus = created.ProviderStatus
        });
    }
}

public class ResendNotificationRouteRequest
{
    public ResendNotificationRouteRequest(int notificationId, ResendNotificationRequest body)
    {
        NotificationId = notificationId;
        Body = body;
    }

    public int NotificationId { get; }
    public ResendNotificationRequest Body { get; }
}

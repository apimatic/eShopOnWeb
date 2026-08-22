using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRouteRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int notificationId, ResendNotificationRequest body, IOrderNotificationService service) =>
            {
                var request = new ResendNotificationRouteRequest
                {
                    NotificationId = notificationId,
                    IdempotencyKey = body.IdempotencyKey
                };
                return await HandleAsync(request, service);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(ResendNotificationRouteRequest request, IOrderNotificationService service)
    {
        return EndpointHelpers.ExecuteAsync(async () =>
        {
            var notification = await service.ResendAsync(request.NotificationId, request.IdempotencyKey);
            return Results.Ok(new ResendNotificationResponse
            {
                NotificationId = notification.Id,
                ProviderMessageSid = notification.ProviderMessageSid,
                ProviderStatus = notification.ProviderStatus
            });
        });
    }
}

public class ResendNotificationRouteRequest : BaseRequest
{
    public int NotificationId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationResponse
{
    public int NotificationId { get; set; }
    public string? ProviderMessageSid { get; set; }
    public string ProviderStatus { get; set; } = string.Empty;
}

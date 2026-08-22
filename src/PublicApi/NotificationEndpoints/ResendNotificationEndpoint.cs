using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int notificationId, ResendNotificationRequest? request, IOrderNotificationService notifications, HttpRequest httpRequest) =>
            {
                request ??= new ResendNotificationRequest();
                request.NotificationId = notificationId;
                if (string.IsNullOrWhiteSpace(request.IdempotencyKey)
                    && httpRequest.Headers.TryGetValue("Idempotency-Key", out var header))
                {
                    request.IdempotencyKey = header.ToString();
                }

                return await HandleAsync(request, notifications);
            })
            .Produces<ResendNotificationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRequest request, IOrderNotificationService notifications)
    {
        var resent = await notifications.ResendAsync(request.NotificationId, request.IdempotencyKey);
        var response = new ResendNotificationResponse(request.CorrelationId())
        {
            NotificationId = resent.Id,
            Notification = OrderNotificationDtoMapper.ToDto(resent)
        };

        return Results.Ok(response);
    }
}

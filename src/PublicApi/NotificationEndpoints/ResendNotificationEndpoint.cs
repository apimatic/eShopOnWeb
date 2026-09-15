using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Messaging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public sealed class ResendNotificationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId:int}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int notificationId,
                ResendNotificationRequest request,
                OrderNotificationService service,
                CancellationToken cancellationToken) =>
            {
                var result = await service.ResendNotificationAsync(notificationId, request.IdempotencyKey, cancellationToken);
                return result.Outcome == OperationOutcome.Success
                    ? Results.Ok(new { notificationId = result.Identifier })
                    : EndpointResultMapper.Map(result);
            })
            .WithTags("Notifications");
    }
}

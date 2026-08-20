using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

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

public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, IOrderLifecycleService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId:int}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int notificationId, ResendNotificationRequest request, IOrderLifecycleService service) =>
            {
                var resent = await service.ResendAsync(notificationId, request.IdempotencyKey);
                return Results.Ok(new ResendNotificationResponse
                {
                    NotificationId = resent.Id,
                    ProviderMessageSid = string.IsNullOrWhiteSpace(resent.ProviderMessageSid) ? null : resent.ProviderMessageSid,
                    ProviderStatus = resent.ProviderStatus
                });
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(ResendNotificationRequest request, IOrderLifecycleService service)
        => Task.FromResult(Results.Ok());
}

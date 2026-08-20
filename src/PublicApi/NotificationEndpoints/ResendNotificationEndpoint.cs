using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, IOrderSmsService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId:int}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, IOrderSmsService service) =>
            {
                var result = await service.ResendAsync(notificationId, request.IdempotencyKey ?? string.Empty);
                return result.ToHttpResult(notification => Results.Ok(new ResendNotificationResponse
                {
                    NotificationId = notification.Id,
                    OriginalNotificationId = notificationId,
                    Status = notification.ProviderStatus,
                    ProviderMessageSid = notification.ProviderMessageSid
                }));
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(ResendNotificationRequest request, IOrderSmsService orderSmsService)
        => Task.FromResult(Results.Ok());
}

public class ResendNotificationRequest
{
    public string? IdempotencyKey { get; set; }
}

public class ResendNotificationResponse
{
    public int NotificationId { get; set; }
    public int OriginalNotificationId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
}

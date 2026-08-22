using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, IShopperOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, IShopperOrderNotificationService service, CancellationToken cancellationToken) =>
            {
                request.NotificationId = notificationId;
                var created = await service.ResendAsync(notificationId, request.IdempotencyKey, cancellationToken);
                return Results.Ok(new ResendNotificationResponse
                {
                    NotificationId = created.Id,
                    Status = created.Status,
                    ProviderSid = created.ProviderSid
                });
            })
            .Produces<ResendNotificationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(ResendNotificationRequest request, IShopperOrderNotificationService service)
        => Task.FromResult(Results.Ok());
}

public class ResendNotificationRequest : BaseRequest
{
    public int NotificationId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationResponse : BaseResponse
{
    public int NotificationId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ProviderSid { get; set; }
}

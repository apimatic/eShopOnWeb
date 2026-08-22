using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, IOperatorNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int notificationId, ResendNotificationRequest request, IOperatorNotificationService service, CancellationToken ct) =>
            {
                var result = await service.ResendAsync(notificationId, request.IdempotencyKey, ct);
                return ResultHttp.ToHttp(result, notification => Results.Ok(new ResendNotificationResponse
                {
                    NotificationId = notification.Id,
                    Status = notification.Status,
                    ProviderSid = notification.ProviderSid
                }));
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(ResendNotificationRequest request, IOperatorNotificationService service)
        => Task.FromResult(Results.Unauthorized());
}

public class ResendNotificationRequest : BaseRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationResponse : BaseResponse
{
    public int NotificationId { get; set; }
    public string? Status { get; set; }
    public string? ProviderSid { get; set; }
}

public class DisposeNotificationContentEndpoint : IEndpoint<IResult, int, IOperatorNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int notificationId, IOperatorNotificationService service, CancellationToken ct) =>
            {
                var result = await service.DisposeContentAsync(notificationId, ct);
                return ResultHttp.ToHttp(result, Results.NoContent);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(int notificationId, IOperatorNotificationService service)
        => Task.FromResult(Results.Unauthorized());
}

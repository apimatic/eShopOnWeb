using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationRequest : BaseRequest
{
    public int NotificationId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationResponse : BaseResponse
{
    public int NotificationId { get; set; }
    public string? ProviderSid { get; set; }
    public string? ProviderStatus { get; set; }
}

public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, IShopperOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, HttpContext httpContext, IShopperOrderService service) =>
            {
                request.NotificationId = notificationId;
                return await HandleAsync(request, httpContext, service);
            })
            .Produces<ResendNotificationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(ResendNotificationRequest request, IShopperOrderService service)
        => HandleAsync(request, null!, service);

    private async Task<IResult> HandleAsync(ResendNotificationRequest request, HttpContext httpContext, IShopperOrderService service)
    {
        var result = await service.ResendAsync(request.NotificationId, request.IdempotencyKey, httpContext.RequestAborted);
        return Results.Ok(new ResendNotificationResponse
        {
            NotificationId = result.Id,
            ProviderSid = result.ProviderSid,
            ProviderStatus = result.ProviderStatus
        });
    }
}

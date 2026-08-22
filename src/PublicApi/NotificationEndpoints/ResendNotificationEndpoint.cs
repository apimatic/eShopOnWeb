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

public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, IShopperOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, IShopperOrderService service) =>
            {
                return await HandleAsync(notificationId, request, service);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(ResendNotificationRequest request, IShopperOrderService service)
        => HandleAsync(0, request, service);

    private async Task<IResult> HandleAsync(int notificationId, ResendNotificationRequest request, IShopperOrderService service)
    {
        var result = await service.ResendAsync(notificationId, request.IdempotencyKey);
        if (!result.IsSuccess)
        {
            return EndpointResultMapper.Map(result);
        }

        return Results.Ok(new ResendNotificationResponse
        {
            NotificationId = result.Value.Id,
            ProviderMessageSid = result.Value.ProviderMessageSid,
            ProviderStatus = result.Value.ProviderStatus
        });
    }
}

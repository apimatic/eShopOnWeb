using System.Threading.Tasks;
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
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, IShopperOrderService orderService, HttpContext httpContext) =>
            {
                request.NotificationId = notificationId;
                return await HandleAsync(request, httpContext, orderService);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(ResendNotificationRequest request, IShopperOrderService orderService)
        => HandleAsync(request, null!, orderService);

    private async Task<IResult> HandleAsync(
        ResendNotificationRequest request,
        HttpContext httpContext,
        IShopperOrderService orderService)
    {
        var response = new ResendNotificationResponse(request.CorrelationId());
        var resent = await orderService.ResendAsync(
            request.NotificationId,
            request.IdempotencyKey,
            httpContext.RequestAborted);

        response.NotificationId = resent.Id;
        response.Status = resent.ProviderStatus;
        response.ProviderSid = resent.ProviderSid;
        return Results.Ok(response);
    }
}

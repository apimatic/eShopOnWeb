using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, IOrderNotificationService service) =>
            {
                return await HandleAsync(request, service, notificationId);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(ResendNotificationRequest request, IOrderNotificationService service) =>
        HandleAsync(request, service, 0);

    private async Task<IResult> HandleAsync(ResendNotificationRequest request, IOrderNotificationService service, int notificationId)
    {
        var result = await service.ResendAsync(notificationId, request.IdempotencyKey, default);
        var response = new ResendNotificationResponse(request.CorrelationId())
        {
            NotificationId = result.Id,
            Status = result.Status,
            ProviderSid = result.ProviderSid
        };
        return Results.Ok(response);
    }
}

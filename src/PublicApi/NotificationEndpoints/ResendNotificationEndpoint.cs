using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, IOperatorOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, IOperatorOrderService operatorOrderService) =>
            {
                return await HandleAsync(request, operatorOrderService, notificationId);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(ResendNotificationRequest request, IOperatorOrderService operatorOrderService)
        => HandleAsync(request, operatorOrderService, notificationId: 0);

    private async Task<IResult> HandleAsync(ResendNotificationRequest request, IOperatorOrderService operatorOrderService, int notificationId)
    {
        var resent = await operatorOrderService.ResendAsync(notificationId, request.IdempotencyKey);
        var response = new ResendNotificationResponse(request.CorrelationId())
        {
            NotificationId = resent.Id,
            Status = resent.ProviderStatus,
            ProviderSid = resent.ProviderMessageSid
        };

        return Results.Ok(response);
    }
}

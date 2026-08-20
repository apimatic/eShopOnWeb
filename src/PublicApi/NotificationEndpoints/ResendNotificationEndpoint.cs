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
            (int notificationId, ResendNotificationRequest request, IOrderNotificationService notificationService) =>
            {
                return await HandleAsync(notificationId, request, notificationService);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(ResendNotificationRequest request, IOrderNotificationService notificationService)
        => Task.FromResult(Results.BadRequest());

    private async Task<IResult> HandleAsync(int notificationId, ResendNotificationRequest request, IOrderNotificationService notificationService)
    {
        var sent = await notificationService.ResendAsync(notificationId, request.IdempotencyKey);
        var response = new ResendNotificationResponse(request.CorrelationId())
        {
            NotificationId = sent.Id,
            ProviderMessageSid = sent.ProviderMessageSid,
            DeliveryStatus = sent.ProviderStatus
        };
        return Results.Ok(response);
    }
}

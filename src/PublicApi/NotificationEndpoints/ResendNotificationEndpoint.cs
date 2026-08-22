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

public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, IOrderNotificationService service, CancellationToken ct) =>
            {
                return await HandleAsync(notificationId, request, service, ct);
            })
            .Produces<ResendNotificationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(ResendNotificationRequest request, IOrderNotificationService service)
        => HandleAsync(0, request, service, CancellationToken.None);

    private async Task<IResult> HandleAsync(
        int notificationId,
        ResendNotificationRequest request,
        IOrderNotificationService service,
        CancellationToken ct)
    {
        var resent = await service.ResendAsync(notificationId, request.IdempotencyKey, ct);
        var response = new ResendNotificationResponse(request.CorrelationId())
        {
            NotificationId = resent.Id,
            Status = resent.ProviderStatus,
            ProviderSid = resent.ProviderSid
        };
        return Results.Ok(response);
    }
}

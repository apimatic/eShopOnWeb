using System;
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
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int notificationId, ResendNotificationRequest request, IOrderNotificationService service, HttpContext http) =>
            {
                request.NotificationId = notificationId;
                return await HandleAsync(request, service, http);
            })
            .Produces<ResendNotificationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(ResendNotificationRequest request, IOrderNotificationService service)
        => throw new NotSupportedException();

    private async Task<IResult> HandleAsync(ResendNotificationRequest request, IOrderNotificationService service, HttpContext http)
    {
        var created = await service.ResendAsync(request.NotificationId, request.IdempotencyKey, http.RequestAborted);
        return Results.Ok(new ResendNotificationResponse(request.CorrelationId())
        {
            NotificationId = created.Id,
            Status = created.ProviderStatus,
            ProviderSid = created.ProviderSid
        });
    }
}

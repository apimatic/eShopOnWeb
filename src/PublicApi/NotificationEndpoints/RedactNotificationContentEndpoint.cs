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

public class RedactNotificationContentEndpoint : IEndpoint<IResult, RedactNotificationContentRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, IOrderNotificationService service, CancellationToken ct) =>
            {
                return await HandleAsync(new RedactNotificationContentRequest(notificationId), service, ct);
            })
            .Produces<RedactNotificationContentResponse>()
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(RedactNotificationContentRequest request, IOrderNotificationService service)
        => HandleAsync(request, service, CancellationToken.None);

    private async Task<IResult> HandleAsync(
        RedactNotificationContentRequest request,
        IOrderNotificationService service,
        CancellationToken ct)
    {
        await service.RedactContentAsync(request.NotificationId, ct);
        var response = new RedactNotificationContentResponse(request.CorrelationId())
        {
            NotificationId = request.NotificationId,
            ContentRedacted = true
        };
        return Results.Ok(response);
    }
}

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

/// <summary>
/// Operator action: disposes of the content of a message about a shopper. Afterwards the text is no
/// longer retrievable from the provider either — while the fact a message was sent, and what became
/// of it, survives. If the provider cannot dispose of it, this reports a failure rather than a false success.
/// </summary>
public class DisposeNotificationContentEndpoint : IEndpoint
{
    private readonly IOrderNotificationService _orderNotificationService;

    public DisposeNotificationContentEndpoint(IOrderNotificationService orderNotificationService)
    {
        _orderNotificationService = orderNotificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, CancellationToken ct) => await HandleAsync(notificationId, ct))
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, CancellationToken ct)
    {
        var disposed = await _orderNotificationService.DisposeContentAsync(notificationId, ct);
        return disposed ? Results.NoContent() : Results.NotFound();
    }
}

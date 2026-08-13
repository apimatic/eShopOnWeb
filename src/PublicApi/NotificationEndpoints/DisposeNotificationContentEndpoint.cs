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
/// Operator action: disposes of the content of a message at the shopper's request. Afterwards the text is
/// no longer retrievable from the provider either, while the fact a message was sent and what became of it
/// survives.
/// </summary>
public class DisposeNotificationContentEndpoint : IEndpoint<IResult, int>
{
    private readonly IOrderNotificationService _service;

    public DisposeNotificationContentEndpoint(IOrderNotificationService service) => _service = service;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, CancellationToken ct) =>
            {
                return await HandleAsync(notificationId, ct);
            })
            .Produces<DisposeNotificationContentResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(int notificationId) => HandleAsync(notificationId, default);

    public async Task<IResult> HandleAsync(int notificationId, CancellationToken ct)
    {
        var notification = await _service.DisposeContentAsync(notificationId, ct);
        if (notification is null) return Results.NotFound();

        return Results.Ok(new DisposeNotificationContentResponse
        {
            NotificationId = notification.Id,
            ContentRedacted = notification.ContentRedacted,
        });
    }
}

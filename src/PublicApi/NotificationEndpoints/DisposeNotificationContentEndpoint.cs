using System;
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
/// Operator action: disposes of a message's content on a shopper's request. Afterwards the text can no
/// longer be retrieved from the provider either, while the fact the message was sent and its outcome survive.
/// </summary>
public class DisposeNotificationContentEndpoint : IEndpoint<IResult, NotificationIdRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, IOrderNotificationService service, CancellationToken ct) =>
            {
                return await ExecuteAsync(new NotificationIdRequest { NotificationId = notificationId }, service, ct);
            })
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(NotificationIdRequest request, IOrderNotificationService service)
        => ExecuteAsync(request, service, CancellationToken.None);

    private static async Task<IResult> ExecuteAsync(NotificationIdRequest request, IOrderNotificationService service, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        var disposed = await service.DisposeContentAsync(request.NotificationId, cts.Token);
        return disposed ? Results.NoContent() : Results.NotFound();
    }
}

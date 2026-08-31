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
/// Disposes of a message's text (operator, on a shopper's request). The body is redacted
/// at the provider as well as locally; the record of the send and its outcome survive.
/// </summary>
public class DeleteNotificationContentEndpoint : IEndpoint<IResult, int, CancellationToken>
{
    private readonly IOrderNotificationService _notificationService;

    public DeleteNotificationContentEndpoint(IOrderNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(notificationId, cancellationToken);
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, CancellationToken cancellationToken)
    {
        await _notificationService.RedactNotificationContentAsync(notificationId, cancellationToken);
        return Results.NoContent();
    }
}

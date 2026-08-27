using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: disposes of a message's content, both in eShop and at the provider,
/// while the fact that a message was sent, and its outcome, survives.
/// </summary>
public class DeleteNotificationContentEndpoint : IEndpoint<IResult, int>
{
    private readonly INotificationService _notificationService;

    public DeleteNotificationContentEndpoint(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId) =>
            {
                return await HandleAsync(notificationId);
            })
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId)
    {
        try
        {
            await _notificationService.DeleteContentAsync(notificationId);
        }
        catch (NotificationNotFoundException)
        {
            return Results.NotFound();
        }

        return Results.Ok(new { notificationId, contentRedacted = true });
    }
}

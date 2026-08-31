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
/// Disposes of a message's text (operator, on a shopper's request) — both in this
/// application and at the provider. The record that a message was sent, and what
/// became of it, survives.
/// </summary>
public class DeleteNotificationContentEndpoint : IEndpoint<IResult, int>
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
            (int notificationId) =>
            {
                return await HandleAsync(notificationId);
            })
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId)
    {
        var redacted = await _notificationService.RedactContentAsync(notificationId);
        return redacted ? Results.NoContent() : Results.NotFound();
    }
}

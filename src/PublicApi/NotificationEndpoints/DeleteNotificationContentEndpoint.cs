using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Notifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action on a shopper's behalf: disposes of a message's content. The body is blanked at the provider
/// first (so it is no longer retrievable there, not merely hidden here) and only then locally. The fact that a
/// message was sent, and what became of it, survives.
/// </summary>
public class DeleteNotificationContentEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, IRepository<Notification> repository, ISmsProvider smsProvider, CancellationToken ct) =>
            {
                return await HandleAsync(notificationId, repository, smsProvider, ct);
            })
            .WithTags("NotificationEndpoints");
    }

    private static async Task<IResult> HandleAsync(
        int notificationId,
        IRepository<Notification> repository,
        ISmsProvider smsProvider,
        CancellationToken ct)
    {
        var notification = await repository.GetByIdAsync(notificationId, ct);
        if (notification is null)
        {
            return Results.NotFound();
        }

        // Redact at the provider first — this must succeed to guarantee the content is truly gone there. If it
        // fails, we do NOT blank locally (which would falsely claim disposal).
        if (notification.ProviderSid is not null && !notification.ContentDisposed)
        {
            try
            {
                await smsProvider.RedactContentAsync(notification.ProviderSid, ct);
            }
            catch (SmsProviderException ex)
            {
                return ProviderErrorResults.From(ex);
            }
        }

        notification.DisposeContent();
        await repository.UpdateAsync(notification, ct);

        return Results.NoContent();
    }
}

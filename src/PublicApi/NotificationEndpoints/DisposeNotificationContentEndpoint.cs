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
/// Operator action: disposes of a message's content on a shopper's request. Afterwards the text is no longer
/// retrievable from the provider either (it is redacted there), while the record that a message was sent and
/// what became of it survives. Restricted to the administrator role.
/// </summary>
public class DisposeNotificationContentEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId:int}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int notificationId,
                IOrderNotificationService notifications,
                CancellationToken ct) =>
            {
                try
                {
                    var existed = await notifications.DisposeContentAsync(notificationId, ct);
                    if (!existed)
                        return Results.NotFound();

                    return Results.Ok(new DisposeContentResponse(notificationId, true));
                }
                catch (SmsGatewayException ex)
                {
                    // The content could not be disposed of at the provider, so we do not claim success.
                    return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
                }
            })
            .Produces<DisposeContentResponse>()
            .WithTags("NotificationEndpoints");
    }
}

public record DisposeContentResponse(int NotificationId, bool ContentDisposed);

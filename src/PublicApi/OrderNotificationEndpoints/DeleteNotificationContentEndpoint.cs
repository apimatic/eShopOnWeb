using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

/// <summary>
/// Operator action: dispose of a message's content — redacted at the provider (not merely hidden
/// locally) while the fact it was sent, and what became of it, survives.
/// </summary>
public class DeleteNotificationContentEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId:int}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, IOrderNotificationService service, System.Threading.CancellationToken ct) =>
            {
                try
                {
                    await service.RedactNotificationContentAsync(notificationId, ct);
                    return Results.NoContent();
                }
                catch (NotificationNotFoundException)
                {
                    return Results.NotFound();
                }
                catch (ProviderGatewayException ex)
                {
                    // Content disposal is the operation itself; a provider failure surfaces to the caller.
                    return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
                }
            })
            .WithTags("OrderNotificationEndpoints");
    }
}

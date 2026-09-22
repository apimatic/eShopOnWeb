using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Twilio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: dispose of a message's content. Afterwards the text is no longer retrievable from the
/// provider either, while the fact that a message was sent and what became of it survives.
/// </summary>
public class RedactNotificationContentEndpoint
    : IEndpoint<IResult, RedactNotificationContentRequest, IOperatorOrderService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, IOperatorOrderService service, CancellationToken ct) =>
            {
                return await HandleAsync(new RedactNotificationContentRequest { NotificationId = notificationId },
                    service, ct);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status502BadGateway)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(RedactNotificationContentRequest request,
        IOperatorOrderService service, CancellationToken ct)
    {
        try
        {
            var found = await service.RedactContentAsync(request.NotificationId, ct);
            return found ? Results.NoContent() : Results.NotFound();
        }
        catch (TwilioProviderException)
        {
            // The provider could not redact the content — do not report success, since the text may remain.
            return Results.StatusCode(StatusCodes.Status502BadGateway);
        }
    }
}

public class RedactNotificationContentRequest : BaseRequest
{
    public int NotificationId { get; set; }
}

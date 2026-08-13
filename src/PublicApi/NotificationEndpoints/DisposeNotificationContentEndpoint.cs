using System.Threading;
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
/// Operator action: dispose of a message's content on request. Afterwards its text is no longer
/// retrievable from the provider either — not merely hidden here — while the fact that a message was
/// sent, and what became of it, survives. Restricted to administrators.
/// </summary>
public class DisposeNotificationContentEndpoint : IEndpoint<IResult, DisposeNotificationContentRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, HttpContext http, IOrderNotificationService service) =>
            {
                return await HandleAsync(new DisposeNotificationContentRequest { NotificationId = notificationId }, service, http.RequestAborted);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(DisposeNotificationContentRequest request, IOrderNotificationService service) =>
        HandleAsync(request, service, CancellationToken.None);

    public async Task<IResult> HandleAsync(DisposeNotificationContentRequest request, IOrderNotificationService service, CancellationToken ct)
    {
        try
        {
            var found = await service.DisposeContentAsync(request.NotificationId, ct);
            return found ? Results.NoContent() : Results.NotFound();
        }
        catch (SmsGatewayException)
        {
            // The provider could not confirm the content was disposed of — do not claim success.
            return Results.Problem(detail: "The content could not be disposed of at the messaging provider. Please try again.",
                statusCode: StatusCodes.Status502BadGateway, title: "Messaging provider unavailable.");
        }
    }
}

public class DisposeNotificationContentRequest : BaseRequest
{
    public int NotificationId { get; set; }
}

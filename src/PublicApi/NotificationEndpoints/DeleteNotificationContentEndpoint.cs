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
/// Operator action: dispose of a message's content on a shopper's behalf. Afterwards the text is no
/// longer retrievable from the provider or from this application, while the record that a message was
/// sent and what became of it survives.
/// </summary>
public class DeleteNotificationContentEndpoint : IEndpoint<IResult, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId:int}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, IOrderNotificationService service, CancellationToken ct) =>
            {
                try
                {
                    var found = await service.RedactContentAsync(notificationId, ct);
                    return found ? Results.NoContent() : Results.NotFound();
                }
                catch (SmsGatewayException)
                {
                    // Disposal is the operation itself: if the provider could not dispose of the content,
                    // do not report success — the content may still be retrievable there.
                    return Results.StatusCode(StatusCodes.Status502BadGateway);
                }
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status502BadGateway)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(IOrderNotificationService service) => Task.FromResult<IResult>(Results.Empty);
}

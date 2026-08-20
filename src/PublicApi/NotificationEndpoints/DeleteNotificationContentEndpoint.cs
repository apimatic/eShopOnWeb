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
/// Operator: dispose of a message's content at the provider (Twilio Body="") while keeping delivery metadata.
/// </summary>
public class DeleteNotificationContentEndpoint : IEndpoint<IResult, DeleteNotificationContentRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int notificationId, IOrderNotificationService service) =>
            {
                return await HandleAsync(new DeleteNotificationContentRequest(notificationId), service);
            })
            .Produces<DeleteNotificationContentResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(DeleteNotificationContentRequest request, IOrderNotificationService service)
    {
        await service.RedactContentAsync(request.NotificationId);
        return Results.Ok(new DeleteNotificationContentResponse(request.CorrelationId())
        {
            NotificationId = request.NotificationId,
            ContentRedacted = true
        });
    }
}

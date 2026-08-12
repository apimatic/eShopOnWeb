using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class DisposeNotificationContentRequest : BaseRequest
{
    public int NotificationId { get; set; }
}

/// <summary>
/// Operator action: disposes of a message's content. The text is redacted at the provider so it is no
/// longer retrievable there, and the local copy is cleared, while the record that a message was sent —
/// and what became of it — survives. Restricted to administrators.
/// </summary>
public class DisposeNotificationContentEndpoint : IEndpoint<IResult, DisposeNotificationContentRequest, INotificationAdminService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, INotificationAdminService service) =>
            {
                return await HandleAsync(new DisposeNotificationContentRequest { NotificationId = notificationId }, service);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status502BadGateway)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(DisposeNotificationContentRequest request, INotificationAdminService service)
    {
        var result = await service.DisposeContentAsync(request.NotificationId);
        return result.Outcome switch
        {
            ContentDisposalOutcome.Disposed => Results.NoContent(),
            ContentDisposalOutcome.NotFound => Results.NotFound(new { error = result.Error }),
            _ => Results.Json(new { error = result.Error }, statusCode: StatusCodes.Status502BadGateway)
        };
    }
}

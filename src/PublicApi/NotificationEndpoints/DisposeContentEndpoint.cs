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
/// Operator action: disposes of a message's content. Afterwards the text is no longer retrievable
/// from the provider (redacted there), while the fact a message was sent and what became of it survives.
/// </summary>
public class DisposeContentEndpoint : IEndpoint<IResult, DisposeContentRequest, ISmsNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ISmsNotificationService service) =>
                await HandleAsync(new DisposeContentRequest { NotificationId = notificationId }, service))
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(DisposeContentRequest request, ISmsNotificationService service)
    {
        var found = await service.DisposeContentAsync(request.NotificationId);
        return found ? Results.NoContent() : Results.NotFound();
    }
}

public class DisposeContentRequest : BaseRequest
{
    public int NotificationId { get; set; }
}

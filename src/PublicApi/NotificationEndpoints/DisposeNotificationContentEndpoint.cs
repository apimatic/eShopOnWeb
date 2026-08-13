using System;
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
    public DisposeNotificationContentRequest(int notificationId) => NotificationId = notificationId;
}

public class DisposeNotificationContentResponse : BaseResponse
{
    public DisposeNotificationContentResponse(Guid correlationId) : base(correlationId) { }
    public DisposeNotificationContentResponse() { }

    public int NotificationId { get; set; }
    public string Status { get; set; } = "ContentDisposed";
}

/// <summary>
/// Operator action: disposes of a message's content at the provider (so its text can no longer be
/// retrieved there) and here, while the fact it was sent and what became of it survive.
/// </summary>
public class DisposeNotificationContentEndpoint : IEndpoint<IResult, DisposeNotificationContentRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                       AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, IOrderNotificationService service) =>
            {
                return await HandleAsync(new DisposeNotificationContentRequest(notificationId), service);
            })
            .Produces<DisposeNotificationContentResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(DisposeNotificationContentRequest request, IOrderNotificationService service)
    {
        await service.DisposeContentAsync(request.NotificationId);
        return Results.Ok(new DisposeNotificationContentResponse(request.CorrelationId()) { NotificationId = request.NotificationId });
    }
}

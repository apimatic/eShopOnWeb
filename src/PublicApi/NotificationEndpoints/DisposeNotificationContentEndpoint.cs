using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class DisposeNotificationContentRequest : BaseRequest
{
    public int NotificationId { get; }

    public DisposeNotificationContentRequest(int notificationId) => NotificationId = notificationId;
}

public class DisposeNotificationContentResponse : BaseResponse
{
    public DisposeNotificationContentResponse(Guid correlationId) : base(correlationId) { }

    public int NotificationId { get; set; }
}

/// <summary>
/// Operator action (on a shopper's request): dispose the content of a message at the provider so its
/// text is no longer retrievable, while the fact it was sent and what became of it survive.
/// </summary>
public class DisposeNotificationContentEndpoint : IEndpoint<IResult, DisposeNotificationContentRequest, IOrderNotificationService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, IOrderNotificationService service, CancellationToken cancellationToken) =>
                await HandleAsync(new DisposeNotificationContentRequest(notificationId), service, cancellationToken))
            .Produces<DisposeNotificationContentResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status502BadGateway)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(DisposeNotificationContentRequest request, IOrderNotificationService service, CancellationToken cancellationToken)
    {
        var outcome = await service.DisposeContentAsync(request.NotificationId, cancellationToken);
        return outcome switch
        {
            DisposeContentOutcome.NotFound => Results.NotFound(),
            DisposeContentOutcome.ProviderFailed => Results.Problem(
                title: "Provider content disposal failed",
                detail: "The message content could not be disposed at the provider. Please try again.",
                statusCode: StatusCodes.Status502BadGateway),
            _ => Results.Ok(new DisposeNotificationContentResponse(request.CorrelationId()) { NotificationId = request.NotificationId })
        };
    }
}

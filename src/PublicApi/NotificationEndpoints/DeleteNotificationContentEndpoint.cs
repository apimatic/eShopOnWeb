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

/// <summary>
/// Operator action: disposes of a message's text. The body is redacted at the
/// provider itself — not merely hidden here — while the record that a message
/// was sent, and its outcome, survives.
/// </summary>
public class DeleteNotificationContentEndpoint : IEndpoint<IResult, DeleteNotificationContentRequest>
{
    private readonly IOrderNotificationService _notificationService;

    public DeleteNotificationContentEndpoint(IOrderNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId) =>
            {
                return await HandleAsync(new DeleteNotificationContentRequest(notificationId));
            })
            .Produces<DeleteNotificationContentResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(DeleteNotificationContentRequest request)
    {
        var redacted = await _notificationService.RedactContentAsync(request.NotificationId);
        if (!redacted)
        {
            return Results.NotFound();
        }

        return Results.Ok(new DeleteNotificationContentResponse(request.CorrelationId())
        {
            NotificationId = request.NotificationId
        });
    }
}

public class DeleteNotificationContentRequest : BaseRequest
{
    public DeleteNotificationContentRequest(int notificationId)
    {
        NotificationId = notificationId;
    }

    public int NotificationId { get; }
}

public class DeleteNotificationContentResponse : BaseResponse
{
    public DeleteNotificationContentResponse(Guid correlationId) : base(correlationId) { }

    public int NotificationId { get; set; }
    public string Status => "ContentRedacted";
}

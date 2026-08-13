using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationRequest
{
    public int NotificationId { get; set; }
    public string? IdempotencyKey { get; set; }
}

public class ResendNotificationResponse
{
    public int NotificationId { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool WasReplay { get; set; }
}

/// <summary>
/// Operator action: re-sends a message that did not reach the shopper. The caller supplies an
/// idempotency key (JSON body <c>idempotencyKey</c> or the <c>Idempotency-Key</c> header); repeating
/// a request under the same key returns the first result without sending again, while a fresh key
/// sends a new message. The response's <c>notificationId</c> is the message the resend produced.
/// POST /api/notifications/{notificationId}/resend
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, INotificationAdminService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest? request, HttpContext httpContext, INotificationAdminService service) =>
            {
                request ??= new ResendNotificationRequest();
                request.NotificationId = notificationId;
                if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
                {
                    request.IdempotencyKey = httpContext.Request.Headers["Idempotency-Key"].FirstOrDefault();
                }
                return await HandleAsync(request, service);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRequest request, INotificationAdminService service)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { message = "An idempotency key is required (body 'idempotencyKey' or 'Idempotency-Key' header)." });
        }

        var result = await service.ResendAsync(request.NotificationId, request.IdempotencyKey);
        if (result is null) return Results.NotFound();

        var response = new ResendNotificationResponse
        {
            NotificationId = result.Notification.Id,
            Status = result.Notification.Status,
            WasReplay = result.WasReplay
        };
        return Results.Ok(response);
    }
}

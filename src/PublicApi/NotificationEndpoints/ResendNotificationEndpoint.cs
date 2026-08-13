using System;
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

public class ResendNotificationRequest : BaseRequest
{
    /// <summary>Caller-supplied idempotency key. Repeating a request under the same key does not send again.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) { }

    /// <summary>The identifier of the message the resend produced.</summary>
    public int NotificationId { get; set; }

    public string Status { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
}

/// <summary>
/// Operator action: re-sends a message that did not reach the shopper. Idempotent on the caller-supplied
/// key — a repeat returns the same produced message without sending again; a fresh key sends anew.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, int, ResendNotificationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, IOrderNotificationService service) =>
                await HandleAsync(notificationId, request, service))
            .Produces<ResendNotificationResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, ResendNotificationRequest request, IOrderNotificationService service)
    {
        // The idempotency key may also be supplied via the standard header.
        var idempotencyKey = request?.IdempotencyKey;
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Results.BadRequest(new { message = "An idempotency key is required." });
        }

        try
        {
            var notification = await service.ResendAsync(notificationId, idempotencyKey);
            if (notification is null)
            {
                return Results.NotFound();
            }

            return Results.Ok(new ResendNotificationResponse(request!.CorrelationId())
            {
                NotificationId = notification.Id,
                Status = notification.Status,
                ProviderMessageSid = notification.ProviderMessageSid
            });
        }
        catch (ContentAlreadyDisposedException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }
}

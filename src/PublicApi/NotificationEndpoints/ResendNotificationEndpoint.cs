using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>Body of POST /api/notifications/{id}/resend.</summary>
public class ResendNotificationRequest
{
    /// <summary>Caller-supplied idempotency key. May also be supplied via the "Idempotency-Key" header.</summary>
    public string? IdempotencyKey { get; set; }

    internal int NotificationId { get; set; }
}

/// <summary>
/// Operator action: re-sends a message that did not reach the shopper. Repeating the request under the
/// same idempotency key returns the message the first attempt produced instead of sending another; a
/// fresh key is a legitimate second attempt.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, IOrderNotificationService>
{
    private const string IdempotencyHeader = "Idempotency-Key";

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId:int}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest? request, HttpContext http, IOrderNotificationService service) =>
            {
                request ??= new ResendNotificationRequest();
                request.NotificationId = notificationId;

                if (string.IsNullOrWhiteSpace(request.IdempotencyKey) &&
                    http.Request.Headers.TryGetValue(IdempotencyHeader, out var headerValue))
                {
                    request.IdempotencyKey = headerValue.ToString();
                }

                return await HandleAsync(request, service);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRequest request, IOrderNotificationService service)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            return Results.BadRequest(new { error = "An idempotency key is required (body 'idempotencyKey' or 'Idempotency-Key' header)." });

        var result = await service.ResendAsync(request.NotificationId, request.IdempotencyKey!);
        if (result is null)
            return Results.NotFound();

        return Results.Ok(new ResendNotificationResponse
        {
            NotificationId = result.Notification.Id,
            Status = result.Notification.ProviderStatus,
            MessageSent = result.MessageSent
        });
    }
}

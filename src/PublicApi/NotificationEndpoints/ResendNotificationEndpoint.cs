using System.Threading;
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
/// Operator action: re-sends a message that did not reach the shopper. The caller-supplied idempotency key
/// (header <c>Idempotency-Key</c> or body field) makes a repeat under the same key a no-op replay, while a
/// fresh key is a genuine new attempt.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest>
{
    private readonly IOrderNotificationService _service;

    public ResendNotificationEndpoint(IOrderNotificationService service) => _service = service;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest? request, HttpContext http, CancellationToken ct) =>
            {
                request ??= new ResendNotificationRequest();
                // Prefer the Idempotency-Key header; fall back to the request body.
                if (http.Request.Headers.TryGetValue("Idempotency-Key", out var headerKey) && !string.IsNullOrWhiteSpace(headerKey))
                    request.IdempotencyKey = headerKey.ToString();

                return await HandleAsync(notificationId, request, ct);
            })
            .Produces<ResendNotificationResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(ResendNotificationRequest request) => HandleAsync(0, request, default);

    public async Task<IResult> HandleAsync(int notificationId, ResendNotificationRequest request, CancellationToken ct)
    {
        var response = new ResendNotificationResponse(request.CorrelationId());
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            return Results.BadRequest("An idempotency key is required (Idempotency-Key header or body field).");

        var outcome = await _service.ResendAsync(notificationId, request.IdempotencyKey!, ct);
        if (outcome is null) return Results.NotFound();

        response.NotificationId = outcome.Notification.Id;
        response.Replayed = outcome.WasReplayed;
        response.Notification = NotificationDto.From(outcome.Notification);
        return Results.Ok(response);
    }
}

using System;
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
/// makes a repeat under the same key a no-op (returning the same notification); a fresh key sends again.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, IOrderNotificationService service, CancellationToken ct) =>
            {
                request.NotificationId = notificationId;
                return await ExecuteAsync(request, service, ct);
            })
            .Produces<ResendNotificationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(ResendNotificationRequest request, IOrderNotificationService service)
        => ExecuteAsync(request, service, CancellationToken.None);

    private static async Task<IResult> ExecuteAsync(ResendNotificationRequest request, IOrderNotificationService service, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            return Results.BadRequest("An idempotency key is required.");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        ResendOutcome? outcome;
        try
        {
            outcome = await service.ResendAsync(request.NotificationId, request.IdempotencyKey, cts.Token);
        }
        catch (InvalidOperationException ex)
        {
            // e.g. the message content has been disposed of and can no longer be re-sent.
            return Results.Conflict(ex.Message);
        }

        if (outcome is null)
            return Results.NotFound();

        return Results.Ok(new ResendNotificationResponse
        {
            NotificationId = outcome.Notification.Id,
            ProviderMessageSid = outcome.Notification.ProviderSid,
            DeliveryStatus = outcome.Notification.DeliveryStatus,
            WasReplay = outcome.WasReplay
        });
    }
}

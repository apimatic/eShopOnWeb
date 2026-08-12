using System;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.NotificationsApi;

// ------------------------------------------------------------------------------------
// Flow 3 — what the operator can do. All three are administrator-only.
// ------------------------------------------------------------------------------------

/// <summary>
/// POST /api/notifications/{notificationId}/resend — re-send a message that did not reach
/// the shopper. Idempotent on a caller-supplied key: a repeat under the same key sends
/// nothing new; a fresh key is a genuine second attempt.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId:int}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int notificationId,
                   [FromHeader(Name = "Idempotency-Key")] string? idempotencyKeyHeader,
                   [FromQuery] string? idempotencyKey,
                   IOrderNotificationService service, CancellationToken ct) =>
            {
                var key = !string.IsNullOrWhiteSpace(idempotencyKeyHeader) ? idempotencyKeyHeader : idempotencyKey;
                if (string.IsNullOrWhiteSpace(key))
                {
                    return Results.BadRequest(new { error = "A caller-supplied idempotency key is required (Idempotency-Key header or idempotencyKey query)." });
                }

                var result = await service.ResendAsync(notificationId, key!, ct);
                if (!result.Found) return Results.NotFound();
                if (result.ContentDisposed)
                {
                    return Results.Conflict(new { error = "The message content has been disposed of and cannot be resent." });
                }

                // notificationId returned is the identifier of the message the resend produced.
                return Results.Ok(new
                {
                    notificationId = result.Notification!.Id,
                    deliveryStatus = result.Notification.DeliveryStatus,
                    providerMessageSid = result.Notification.ProviderMessageSid
                });
            })
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("NotificationEndpoints")
            .WithMetadata(new SwaggerOperationAttribute("Resend a message that did not reach the shopper (operator)"));
    }
}

/// <summary>
/// DELETE /api/notifications/{notificationId}/content — dispose of a message's content.
/// The text is redacted at the provider too; the fact it was sent and what became of it survives.
/// </summary>
public class DeleteNotificationContentEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId:int}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int notificationId, IOrderNotificationService service, CancellationToken ct) =>
            {
                try
                {
                    await service.RedactContentAsync(notificationId, ct);
                    return Results.Ok(new { notificationId, contentRedacted = true });
                }
                catch (NotificationNotFoundException) { return Results.NotFound(); }
            })
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints")
            .WithMetadata(new SwaggerOperationAttribute("Dispose of a message's content at the provider (operator)"));
    }
}

/// <summary>
/// GET /api/notifications/reconciliation?from={from}&amp;to={to} — line up the provider's
/// record of messages sent from this application's configured number against eShop's record.
/// </summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (DateTimeOffset from, DateTimeOffset to,
                   IOrderNotificationService service, CancellationToken ct) =>
            {
                if (to < from)
                {
                    return Results.BadRequest(new { error = "'to' must be on or after 'from'." });
                }

                var report = await service.ReconcileAsync(from, to, ct);
                return Results.Ok(report);
            })
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints")
            .WithMetadata(new SwaggerOperationAttribute("Reconcile provider vs eShop messages over a date range (operator)"));
    }
}

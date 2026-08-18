using System;
using System.Globalization;
using System.Linq;
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

// These are operator actions, restricted to the administrator role.

/// <summary>Operator action: re-sends a message that did not reach the shopper, under a caller-supplied idempotency key.</summary>
public class ResendNotificationEndpoint : IEndpoint
{
    public const string IdempotencyHeader = "Idempotency-Key";

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId:int}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int notificationId,
                ResendNotificationRequest? request,
                HttpContext http,
                INotificationOperationsService service,
                CancellationToken cancellationToken) =>
            {
                // The idempotency key may arrive as a header or in the body.
                var key = http.Request.Headers[IdempotencyHeader].FirstOrDefault();
                if (string.IsNullOrWhiteSpace(key))
                {
                    key = request?.IdempotencyKey;
                }
                if (string.IsNullOrWhiteSpace(key))
                {
                    return Results.BadRequest(new { errors = new[] { $"An idempotency key is required (header '{IdempotencyHeader}' or body 'idempotencyKey')." } });
                }

                var result = await service.ResendAsync(notificationId, key, cancellationToken);
                return Results.Ok(new ResendNotificationResponse(result.Notification.Id, result.Reused));
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }
}

/// <summary>Operator action: disposes of a message's content at the provider and locally, keeping its record.</summary>
public class DisposeNotificationContentEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId:int}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int notificationId,
                INotificationOperationsService service,
                CancellationToken cancellationToken) =>
            {
                await service.DisposeContentAsync(notificationId, cancellationToken);
                return Results.NoContent();
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }
}

/// <summary>Operator action: lines up the provider's record of this sending number's messages against eShop's.</summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                string? from,
                string? to,
                INotificationOperationsService service,
                CancellationToken cancellationToken) =>
            {
                if (!TryParseIso(from, out var fromDate) || !TryParseIso(to, out var toDate))
                {
                    return Results.BadRequest(new { errors = new[] { "'from' and 'to' must be ISO-8601 date-times." } });
                }
                if (toDate < fromDate)
                {
                    return Results.BadRequest(new { errors = new[] { "'to' must not be earlier than 'from'." } });
                }

                var report = await service.ReconcileAsync(fromDate, toDate, cancellationToken);
                return Results.Ok(report);
            })
            .Produces<ReconciliationReport>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    private static bool TryParseIso(string? value, out DateTimeOffset result)
    {
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result);
    }
}

public record ResendNotificationRequest(string? IdempotencyKey);
public record ResendNotificationResponse(int NotificationId, bool Reused);

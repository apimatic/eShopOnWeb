using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public sealed record ResendNotificationRequest(string IdempotencyKey);

public sealed class ResendNotificationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId:int}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int notificationId,
                ResendNotificationRequest request,
                OrderNotificationService notifications,
                CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 128)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["idempotencyKey"] = new[] { "An idempotency key of at most 128 characters is required." }
                    });
                }

                var result = await notifications.ResendAsync(
                    notificationId,
                    request.IdempotencyKey.Trim(),
                    cancellationToken);
                if (result.NotificationId.HasValue)
                {
                    return Results.Created(
                        $"/api/notifications/{result.NotificationId.Value}",
                        new { notificationId = result.NotificationId.Value });
                }

                return result.Failure switch
                {
                    ResendFailure.NotFound => Results.NotFound(),
                    ResendFailure.IdempotencyKeyConflict => Results.Conflict(new { message = "That idempotency key was used for another notification." }),
                    ResendFailure.ContentDisposed => Results.Conflict(new { message = "Disposed message content cannot be resent." }),
                    ResendFailure.ContactRemoved => Results.Conflict(new { message = "The destination is no longer registered." }),
                    _ => Results.Conflict(new { message = "Only failed or undelivered messages can be resent." })
                };
            })
            .Produces(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .RequireAuthorization()
            .WithTags("NotificationOperatorEndpoints");
    }
}

public sealed class DisposeNotificationContentEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId:int}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int notificationId,
                OrderNotificationService notifications,
                CancellationToken cancellationToken) =>
            {
                var result = await notifications.DisposeContentAsync(notificationId, cancellationToken);
                return result switch
                {
                    ContentDisposalResult.NotFound => Results.NotFound(),
                    ContentDisposalResult.ProviderUnavailable => Results.Problem(
                        "The provider has not confirmed content disposal.",
                        statusCode: StatusCodes.Status503ServiceUnavailable),
                    _ => Results.NoContent()
                };
            })
            .RequireAuthorization()
            .WithTags("NotificationOperatorEndpoints");
    }
}

public sealed class ReconcileNotificationsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                DateTimeOffset from,
                DateTimeOffset to,
                OrderNotificationService notifications,
                CancellationToken cancellationToken) =>
            {
                if (from > to)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["range"] = new[] { "from must be earlier than or equal to to." }
                    });
                }

                ReconciliationData data;
                try
                {
                    data = await notifications.ReconcileAsync(from, to, cancellationToken);
                }
                catch (SmsProviderException)
                {
                    return Results.Problem(
                        "The provider reconciliation could not be completed.",
                        statusCode: StatusCodes.Status503ServiceUnavailable);
                }

                var localBySid = data.LocalNotifications
                    .Where(x => x.ProviderMessageSid is not null)
                    .ToDictionary(x => x.ProviderMessageSid!, StringComparer.Ordinal);
                var providerSids = data.ProviderMessages.Select(x => x.Sid).ToHashSet(StringComparer.Ordinal);
                var entries = new List<object>();

                foreach (var providerMessage in data.ProviderMessages.OrderBy(x => x.DateSent ?? x.DateCreated))
                {
                    localBySid.TryGetValue(providerMessage.Sid, out var local);
                    entries.Add(new
                    {
                        presence = local is null ? "provider-only" : "matched",
                        providerMessageSid = providerMessage.Sid,
                        notificationId = local?.Id,
                        providerStatus = providerMessage.Status,
                        applicationStatus = local?.ProviderStatus,
                        providerErrorCode = providerMessage.ErrorCode,
                        occurredAt = providerMessage.DateSent ?? providerMessage.DateCreated
                    });
                }

                foreach (var local in data.LocalNotifications
                             .Where(x => (x.ProviderMessageSid is null || !providerSids.Contains(x.ProviderMessageSid)) &&
                                         x.CreatedAt >= from && x.CreatedAt <= to)
                             .OrderBy(x => x.CreatedAt))
                {
                    entries.Add(new
                    {
                        presence = "application-only",
                        providerMessageSid = local.ProviderMessageSid,
                        notificationId = (int?)local.Id,
                        providerStatus = (string?)null,
                        applicationStatus = local.ProviderStatus,
                        providerErrorCode = local.ProviderErrorCode,
                        occurredAt = (DateTimeOffset?)local.CreatedAt
                    });
                }

                return Results.Ok(new
                {
                    from,
                    to,
                    entries,
                    counts = new
                    {
                        matched = entries.Count(x => ReadPresence(x) == "matched"),
                        providerOnly = entries.Count(x => ReadPresence(x) == "provider-only"),
                        applicationOnly = entries.Count(x => ReadPresence(x) == "application-only")
                    }
                });
            })
            .RequireAuthorization()
            .WithTags("NotificationOperatorEndpoints");
    }

    private static string? ReadPresence(object entry)
    {
        return entry.GetType().GetProperty("presence")?.GetValue(entry) as string;
    }
}

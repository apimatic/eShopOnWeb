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
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

public sealed record ResendNotificationRequest(string IdempotencyKey);
public sealed record ResendNotificationResponse(int NotificationId);
public sealed record ReconciliationEntry(
    string Match,
    int? NotificationId,
    string? ProviderMessageSid,
    string? ApplicationStatus,
    string? ProviderStatus,
    int? ProviderErrorCode,
    DateTimeOffset? ProviderCreatedAt,
    DateTimeOffset? ProviderSentAt);
public sealed record ReconciliationResponse(DateTimeOffset From, DateTimeOffset To, IReadOnlyList<ReconciliationEntry> Messages);

public sealed class ResendNotificationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId:int}/resend",
                [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                    int notificationId,
                    ResendNotificationRequest request,
                    CatalogContext context,
                    IOrderNotificationService notificationService,
                    CancellationToken cancellationToken) =>
                {
                    if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 128)
                        return Results.BadRequest(new { error = "idempotencyKey is required and may contain at most 128 characters." });

                    var original = await context.OrderNotifications.FindAsync(new object[] { notificationId }, cancellationToken);
                    if (original is null) return Results.NotFound();

                    await notificationService.RefreshAsync(new[] { original }, cancellationToken);
                    try
                    {
                        var resent = await notificationService.ResendAsync(
                            original, request.IdempotencyKey.Trim(), cancellationToken);
                        return Results.Created(
                            $"/api/orders/{resent.OrderId}/notifications",
                            new ResendNotificationResponse(resent.Id));
                    }
                    catch (InvalidOperationException exception)
                    {
                        return Results.Conflict(new { error = exception.Message });
                    }
                })
            .Produces<ResendNotificationResponse>(StatusCodes.Status201Created)
            .WithTags("OrderNotificationEndpoints");
    }
}

public sealed class DisposeNotificationContentEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId:int}/content",
                [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                    int notificationId,
                    CatalogContext context,
                    IOrderNotificationService notificationService,
                    CancellationToken cancellationToken) =>
                {
                    var notification = await context.OrderNotifications.FindAsync(new object[] { notificationId }, cancellationToken);
                    if (notification is null) return Results.NotFound();

                    try
                    {
                        await notificationService.RedactAsync(notification, cancellationToken);
                    }
                    catch (Exception) when (!cancellationToken.IsCancellationRequested)
                    {
                        return Results.Problem(
                            "The provider could not dispose of the message content.",
                            statusCode: StatusCodes.Status502BadGateway);
                    }

                    return Results.NoContent();
                })
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("OrderNotificationEndpoints");
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
                    CatalogContext context,
                    ISmsProvider provider,
                    CancellationToken cancellationToken) =>
                {
                    if (from > to) return Results.BadRequest(new { error = "from must be at or before to." });

                    IReadOnlyList<ProviderMessage> providerMessages;
                    try
                    {
                        providerMessages = await provider.ListAsync(from, to, cancellationToken);
                    }
                    catch (Exception) when (!cancellationToken.IsCancellationRequested)
                    {
                        return Results.Problem(
                            "The provider reconciliation data could not be retrieved.",
                            statusCode: StatusCodes.Status502BadGateway);
                    }

                    var local = await context.OrderNotifications
                        .AsNoTracking()
                        .Where(notification =>
                            (notification.ProviderSentAt ?? notification.ProviderCreatedAt ?? notification.CreatedAt) >= from &&
                            (notification.ProviderSentAt ?? notification.ProviderCreatedAt ?? notification.CreatedAt) <= to)
                        .ToListAsync(cancellationToken);
                    var localBySid = local
                        .Where(notification => notification.ProviderMessageSid is not null)
                        .ToDictionary(notification => notification.ProviderMessageSid!, StringComparer.Ordinal);
                    var providerBySid = providerMessages.ToDictionary(message => message.Sid, StringComparer.Ordinal);

                    var entries = new List<ReconciliationEntry>();
                    foreach (var providerMessage in providerMessages)
                    {
                        localBySid.TryGetValue(providerMessage.Sid, out var notification);
                        entries.Add(new ReconciliationEntry(
                            notification is null ? "provider-only" : "matched",
                            notification?.Id,
                            providerMessage.Sid,
                            notification?.ProviderStatus,
                            providerMessage.Status,
                            providerMessage.ErrorCode,
                            providerMessage.CreatedAt,
                            providerMessage.SentAt));
                    }

                    entries.AddRange(local
                        .Where(notification => notification.ProviderMessageSid is null || !providerBySid.ContainsKey(notification.ProviderMessageSid))
                        .Select(notification => new ReconciliationEntry(
                            "application-only",
                            notification.Id,
                            notification.ProviderMessageSid,
                            notification.ProviderStatus,
                            null,
                            notification.ProviderErrorCode,
                            notification.ProviderCreatedAt,
                            notification.ProviderSentAt)));

                    return Results.Ok(new ReconciliationResponse(from, to, entries));
                })
            .Produces<ReconciliationResponse>()
            .WithTags("OrderNotificationEndpoints");
    }
}

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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

public sealed class NotificationOperatorEndpoints : IEndpoint
{
    private const string AuthenticationScheme = JwtBearerDefaults.AuthenticationScheme;
    private const string AdministratorRole = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId:int}/resend",
            [Authorize(Roles = AdministratorRole, AuthenticationSchemes = AuthenticationScheme)] async (
                int notificationId,
                ResendNotificationRequest request,
                OrderNotificationManager manager,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var newNotificationId = await manager.ResendAsync(notificationId, request.IdempotencyKey ?? string.Empty, cancellationToken);
                    return Results.Created($"/api/notifications/{newNotificationId}", new ResendNotificationResponse(newNotificationId));
                }
                catch (KeyNotFoundException) { return Results.NotFound(); }
                catch (ArgumentException exception) { return Results.BadRequest(new { message = exception.Message }); }
                catch (InvalidOperationException exception) { return Results.Conflict(new { message = exception.Message }); }
            })
            .Produces<ResendNotificationResponse>(StatusCodes.Status201Created)
            .RequireAuthorization()
            .WithTags("NotificationOperatorEndpoints");

        app.MapDelete("api/notifications/{notificationId:int}/content",
            [Authorize(Roles = AdministratorRole, AuthenticationSchemes = AuthenticationScheme)] async (
                int notificationId,
                OrderNotificationManager manager,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    await manager.RedactAsync(notificationId, cancellationToken);
                    return Results.NoContent();
                }
                catch (KeyNotFoundException) { return Results.NotFound(); }
                catch (TwilioApiException) { return Results.Problem("The provider could not dispose of the message content.", statusCode: StatusCodes.Status502BadGateway); }
            })
            .RequireAuthorization()
            .WithTags("NotificationOperatorEndpoints");

        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = AdministratorRole, AuthenticationSchemes = AuthenticationScheme)] async (
                DateTimeOffset? from,
                DateTimeOffset? to,
                CatalogContext db,
                ITwilioMessagingService twilio,
                CancellationToken cancellationToken) =>
            {
                if (!from.HasValue || !to.HasValue || from > to)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["range"] = new[] { "Valid ISO-8601 from and to values are required, and from must not be after to." } });
                }

                IReadOnlyList<TwilioMessageState> providerMessages;
                try
                {
                    providerMessages = await twilio.ListAsync(from.Value, to.Value, cancellationToken);
                }
                catch
                {
                    return Results.Problem("The provider reconciliation data is unavailable.", statusCode: StatusCodes.Status502BadGateway);
                }

                var localMessages = await db.OrderNotifications
                    .Where(x => x.CreatedAt >= from.Value && x.CreatedAt <= to.Value)
                    .OrderBy(x => x.CreatedAt)
                    .ToListAsync(cancellationToken);
                var localMessagesCreatedInRange = localMessages.ToList();
                var knownLocalIds = localMessages.Select(x => x.Id).ToHashSet();
                foreach (var providerSidBatch in providerMessages.Select(x => x.Sid).Distinct().Chunk(500))
                {
                    var localMatches = await db.OrderNotifications
                        .Where(x => x.ProviderMessageSid != null && providerSidBatch.Contains(x.ProviderMessageSid))
                        .ToListAsync(cancellationToken);
                    localMessages.AddRange(localMatches.Where(x => knownLocalIds.Add(x.Id)));
                }
                var localBySid = localMessages
                    .Where(x => x.ProviderMessageSid != null)
                    .ToDictionary(x => x.ProviderMessageSid!, StringComparer.Ordinal);
                var providerBySid = providerMessages.ToDictionary(x => x.Sid, StringComparer.Ordinal);
                var entries = new List<ReconciliationEntryResponse>();

                foreach (var provider in providerMessages.OrderBy(x => x.DateSent).ThenBy(x => x.Sid))
                {
                    localBySid.TryGetValue(provider.Sid, out var local);
                    entries.Add(new ReconciliationEntryResponse(
                        local is null ? "providerOnly" : "matched",
                        provider.Sid,
                        local?.Id,
                        local?.OrderId,
                        local?.ProviderStatus,
                        provider.Status,
                        local?.CreatedAt,
                        provider.DateSent));
                }

                entries.AddRange(localMessagesCreatedInRange
                    .Where(x => x.ProviderMessageSid is null || !providerBySid.ContainsKey(x.ProviderMessageSid))
                    .Select(x => new ReconciliationEntryResponse(
                        "applicationOnly",
                        x.ProviderMessageSid,
                        x.Id,
                        x.OrderId,
                        x.ProviderStatus,
                        null,
                        x.CreatedAt,
                        null)));

                return Results.Ok(new ReconciliationResponse(from.Value, to.Value, entries));
            })
            .Produces<ReconciliationResponse>()
            .ProducesValidationProblem()
            .RequireAuthorization()
            .WithTags("NotificationOperatorEndpoints");
    }
}

public sealed record ResendNotificationRequest(string? IdempotencyKey);
public sealed record ResendNotificationResponse(int NotificationId);
public sealed record ReconciliationResponse(DateTimeOffset From, DateTimeOffset To, IReadOnlyList<ReconciliationEntryResponse> Messages);
public sealed record ReconciliationEntryResponse(
    string Match,
    string? ProviderMessageSid,
    int? NotificationId,
    int? OrderId,
    string? ApplicationStatus,
    string? ProviderStatus,
    DateTimeOffset? ApplicationCreatedAt,
    DateTimeOffset? ProviderDateSent);

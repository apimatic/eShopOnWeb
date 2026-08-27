using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconciliationEntryDto
{
    public string? ProviderMessageSid { get; set; }
    public string? ProviderStatus { get; set; }
    public DateTimeOffset? ProviderDateSent { get; set; }
    public int? NotificationId { get; set; }
    public int? OrderId { get; set; }
    public string? LocalStatus { get; set; }

    /// <summary>matched | providerOnly | localOnly</summary>
    public string Match { get; set; } = string.Empty;
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int ProviderMessageCount { get; set; }
    public int LocalNotificationCount { get; set; }
    public List<ReconciliationEntryDto> Entries { get; set; } = new();
}

/// <summary>
/// Operator action: lines up the provider's own record of messages sent from this
/// application's configured sending number (requested server-side from the provider)
/// against what eShop believes it sent, over the whole requested range.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, DateTimeOffset, DateTimeOffset>
{
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly ISmsProvider _smsProvider;

    public ReconciliationEndpoint(IRepository<OrderNotification> notificationRepository, ISmsProvider smsProvider)
    {
        _notificationRepository = notificationRepository;
        _smsProvider = smsProvider;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset? from, DateTimeOffset? to) =>
            {
                if (from is null || to is null)
                {
                    return Results.BadRequest(new { error = "from and to (ISO-8601 date-times) are required." });
                }
                return await HandleAsync(from.Value, to.Value);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(DateTimeOffset from, DateTimeOffset to)
    {
        if (to < from)
        {
            return Results.BadRequest(new { error = "to must not be earlier than from." });
        }

        var providerMessages = await _smsProvider.ListMessagesFromConfiguredNumberAsync(from, to);
        var localNotifications = await _notificationRepository.ListAsync(
            new NotificationsCreatedInRangeSpecification(from, to));

        var localBySid = localNotifications
            .Where(n => n.ProviderMessageSid is not null)
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var entries = new List<ReconciliationEntryDto>();
        var matchedSids = new HashSet<string>();

        foreach (var message in providerMessages)
        {
            if (localBySid.TryGetValue(message.MessageSid, out var local))
            {
                matchedSids.Add(message.MessageSid);
                // Keep the local record in step with the provider's current outcome.
                if (message.Status is not null && message.Status != local.Status)
                {
                    local.UpdateProviderStatus(message.Status, message.ErrorCode, message.ErrorMessage);
                    await _notificationRepository.UpdateAsync(local);
                }
                entries.Add(new ReconciliationEntryDto
                {
                    ProviderMessageSid = message.MessageSid,
                    ProviderStatus = message.Status,
                    ProviderDateSent = message.DateSent,
                    NotificationId = local.Id,
                    OrderId = local.OrderId,
                    LocalStatus = local.Status,
                    Match = "matched"
                });
            }
            else
            {
                // The provider knows about this message; eShop doesn't.
                entries.Add(new ReconciliationEntryDto
                {
                    ProviderMessageSid = message.MessageSid,
                    ProviderStatus = message.Status,
                    ProviderDateSent = message.DateSent,
                    Match = "providerOnly"
                });
            }
        }

        foreach (var local in localNotifications.Where(n => n.ProviderMessageSid is null || !matchedSids.Contains(n.ProviderMessageSid)))
        {
            // The provider's list is filtered by DateSent, which never-sent messages
            // (scheduled, canceled before send) do not have. Ask the provider directly
            // before concluding it has no record of the message.
            ProviderMessageResult? direct = null;
            if (local.ProviderMessageSid is not null)
            {
                direct = await _smsProvider.GetMessageAsync(local.ProviderMessageSid);
                if (direct?.Status is not null && direct.Status != local.Status)
                {
                    local.UpdateProviderStatus(direct.Status, direct.ErrorCode, direct.ErrorMessage);
                    await _notificationRepository.UpdateAsync(local);
                }
            }

            entries.Add(direct is not null
                ? new ReconciliationEntryDto
                {
                    ProviderMessageSid = local.ProviderMessageSid,
                    ProviderStatus = direct.Status,
                    ProviderDateSent = direct.DateSent,
                    NotificationId = local.Id,
                    OrderId = local.OrderId,
                    LocalStatus = local.Status,
                    Match = "matched"
                }
                // eShop believes it sent this; the provider has no record of it.
                : new ReconciliationEntryDto
                {
                    ProviderMessageSid = local.ProviderMessageSid,
                    NotificationId = local.Id,
                    OrderId = local.OrderId,
                    LocalStatus = local.Status,
                    Match = "localOnly"
                });
        }

        return Results.Ok(new ReconciliationResponse
        {
            From = from,
            To = to,
            ProviderMessageCount = providerMessages.Count,
            LocalNotificationCount = localNotifications.Count,
            Entries = entries.OrderBy(e => e.ProviderDateSent ?? DateTimeOffset.MinValue).ToList()
        });
    }
}

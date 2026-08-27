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
using Microsoft.eShopWeb.Infrastructure.Services.Twilio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: reconciliation report. Lists the provider's own record of messages
/// for a date range — requested from the provider already filtered to this application's
/// configured sending number (Twilio:FromNumber), since the account carries other
/// traffic — and lines them up against what eShop believes it sent.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, DateTime, DateTime>
{
    private readonly IMessageProvider _messageProvider;
    private readonly IReadRepository<OrderNotification> _notificationRepository;
    private readonly TwilioSettings _twilioSettings;

    public ReconciliationEndpoint(
        IMessageProvider messageProvider,
        IReadRepository<OrderNotification> notificationRepository,
        TwilioSettings twilioSettings)
    {
        _messageProvider = messageProvider;
        _notificationRepository = notificationRepository;
        _twilioSettings = twilioSettings;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTime from, DateTime to) =>
            {
                return await HandleAsync(from, to);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(DateTime from, DateTime to)
    {
        var fromOffset = new DateTimeOffset(from, TimeSpan.Zero);
        var toOffset = new DateTimeOffset(to, TimeSpan.Zero);
        if (toOffset < fromOffset)
        {
            return Results.BadRequest(new { message = "'to' must not be earlier than 'from'." });
        }

        var providerMessages = await _messageProvider.ListMessagesAsync(_twilioSettings.FromNumber, fromOffset, toOffset);
        var localNotifications = await _notificationRepository.ListAsync(new NotificationsInRangeSpecification(fromOffset, toOffset));

        var localByProviderSid = localNotifications
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var entries = new List<ReconciliationEntry>();

        foreach (var message in providerMessages)
        {
            var known = localByProviderSid.TryGetValue(message.Sid, out var local);
            entries.Add(new ReconciliationEntry
            {
                ProviderMessageSid = message.Sid,
                NotificationId = local?.Id,
                OrderId = local?.OrderId,
                ProviderStatus = message.Status,
                LocalStatus = local?.ProviderStatus,
                DateSent = message.DateSent,
                Match = known ? "matched" : "providerOnly"
            });
            if (known)
            {
                localByProviderSid.Remove(message.Sid);
            }
        }

        // Anything left over is something eShop recorded in the range that the
        // provider has no matching message for (or that never reached the provider).
        foreach (var local in localNotifications)
        {
            if (local.ProviderMessageSid != null && !localByProviderSid.ContainsKey(local.ProviderMessageSid))
            {
                continue;
            }
            entries.Add(new ReconciliationEntry
            {
                ProviderMessageSid = local.ProviderMessageSid,
                NotificationId = local.Id,
                OrderId = local.OrderId,
                ProviderStatus = null,
                LocalStatus = local.ProviderStatus,
                DateSent = null,
                Match = "localOnly"
            });
        }

        var response = new ReconciliationResponse
        {
            From = fromOffset,
            To = toOffset,
            FromNumber = _twilioSettings.FromNumber,
            Entries = entries.OrderBy(e => e.DateSent).ToList()
        };
        return Results.Ok(response);
    }
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public List<ReconciliationEntry> Entries { get; set; } = new();
}

public class ReconciliationEntry
{
    public string? ProviderMessageSid { get; set; }
    public int? NotificationId { get; set; }
    public int? OrderId { get; set; }
    public string? ProviderStatus { get; set; }
    public string? LocalStatus { get; set; }
    public DateTimeOffset? DateSent { get; set; }

    /// <summary>matched | providerOnly (provider knows it, eShop doesn't) | localOnly (the reverse).</summary>
    public string Match { get; set; } = string.Empty;
}

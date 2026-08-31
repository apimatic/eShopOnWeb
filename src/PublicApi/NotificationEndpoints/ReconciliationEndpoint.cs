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
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.Notifications;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconciliationEntry
{
    public string? ProviderMessageSid { get; set; }
    public int? NotificationId { get; set; }
    public string? Kind { get; set; }
    public string? ProviderStatus { get; set; }
    public string? LocalStatus { get; set; }
    public string? To { get; set; }
    public string? DateSent { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string SendingNumber { get; set; } = string.Empty;
    public List<ReconciliationEntry> Matched { get; set; } = new();
    public List<ReconciliationEntry> ProviderOnly { get; set; } = new();
    public List<ReconciliationEntry> LocalOnly { get; set; } = new();
}

/// <summary>
/// Reconciliation report (operator): the provider's own record of messages sent from this
/// application's configured sending number over [from, to], lined up against what eShop
/// believes it sent. Messages on the account from other senders are excluded by asking the
/// provider for only this number's messages.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, DateTimeOffset, DateTimeOffset, CancellationToken>
{
    private readonly TwilioMessagingService _messaging;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly TwilioOptions _options;

    public ReconciliationEndpoint(TwilioMessagingService messaging,
        IRepository<OrderNotification> notifications,
        IOptions<TwilioOptions> options)
    {
        _messaging = messaging;
        _notifications = notifications;
        _options = options.Value;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
            {
                return await HandleAsync(from, to, ct);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        if (from == default || to == default || from >= to)
        {
            return Results.BadRequest(new { message = "Query parameters 'from' and 'to' are required ISO-8601 date-times, with 'from' before 'to'." });
        }

        IReadOnlyList<ProviderMessage> providerMessages;
        try
        {
            providerMessages = await _messaging.ListMessagesAsync(from, to, ct);
        }
        catch (MessagingException)
        {
            return Results.Problem("The provider's message records could not be retrieved.", statusCode: 502);
        }

        var localNotifications = await _notifications.ListAsync(
            new NotificationsCreatedInRangeSpecification(from, to), ct);

        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .GroupBy(m => m.Sid)
            .ToDictionary(g => g.Key, g => g.First());
        var localBySid = localNotifications
            .Where(n => n.ProviderMessageSid is not null)
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var response = new ReconciliationResponse
        {
            From = from,
            To = to,
            SendingNumber = _options.FromNumber
        };

        foreach (var (sid, providerMessage) in providerBySid)
        {
            if (localBySid.TryGetValue(sid, out var local))
            {
                response.Matched.Add(new ReconciliationEntry
                {
                    ProviderMessageSid = sid,
                    NotificationId = local.Id,
                    Kind = local.Kind.ToString(),
                    ProviderStatus = providerMessage.Status,
                    LocalStatus = local.Status,
                    To = providerMessage.To,
                    DateSent = providerMessage.DateSent
                });
            }
            else
            {
                response.ProviderOnly.Add(new ReconciliationEntry
                {
                    ProviderMessageSid = sid,
                    ProviderStatus = providerMessage.Status,
                    To = providerMessage.To,
                    DateSent = providerMessage.DateSent
                });
            }
        }

        foreach (var local in localNotifications)
        {
            if (local.ProviderMessageSid is null || !providerBySid.ContainsKey(local.ProviderMessageSid))
            {
                response.LocalOnly.Add(new ReconciliationEntry
                {
                    NotificationId = local.Id,
                    Kind = local.Kind.ToString(),
                    ProviderMessageSid = local.ProviderMessageSid,
                    LocalStatus = local.Status
                });
            }
        }

        return Results.Ok(response);
    }
}

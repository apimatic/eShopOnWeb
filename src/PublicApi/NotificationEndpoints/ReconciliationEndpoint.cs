using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: lines up the provider's own record of messages for a date
/// range against what eShop believes it sent. Only messages from this
/// application's configured sending number are asked for, so unrelated traffic
/// on the same provider account never appears.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest>
{
    private readonly ISmsGateway _smsGateway;
    private readonly IRepository<OrderNotification> _notificationRepository;

    public ReconciliationEndpoint(ISmsGateway smsGateway, IRepository<OrderNotification> notificationRepository)
    {
        _smsGateway = smsGateway;
        _notificationRepository = notificationRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to) =>
            {
                return await HandleAsync(new ReconciliationRequest(from, to));
            })
            .Produces<ReconciliationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request)
    {
        if (!DateTimeOffset.TryParse(request.From, out var fromUtc) ||
            !DateTimeOffset.TryParse(request.To, out var toUtc))
        {
            return Results.BadRequest(new { error = "from and to must be ISO-8601 date-times." });
        }

        if (toUtc < fromUtc)
        {
            return Results.BadRequest(new { error = "to must not be earlier than from." });
        }

        var providerMessages = await _smsGateway.ListMessagesAsync(fromUtc, toUtc);
        var localNotifications = await _notificationRepository.ListAsync(
            new NotificationsCreatedBetweenSpecification(fromUtc, toUtc));

        var localBySid = localNotifications
            .Where(n => n.MessageSid is not null)
            .GroupBy(n => n.MessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var entries = new List<ReconciliationEntry>();

        foreach (var message in providerMessages)
        {
            var matched = localBySid.TryGetValue(message.MessageSid, out var local);
            entries.Add(new ReconciliationEntry
            {
                MessageSid = message.MessageSid,
                To = message.To,
                ProviderStatus = message.Status,
                ProviderDateSent = message.DateSent,
                Match = matched ? "Matched" : "ProviderOnly",
                NotificationId = matched ? local!.Id : null,
                LocalStatus = matched ? local!.Status : null
            });
        }

        var providerSids = providerMessages.Select(m => m.MessageSid).ToHashSet();
        foreach (var local in localNotifications.Where(n => n.MessageSid is null || !providerSids.Contains(n.MessageSid)))
        {
            entries.Add(new ReconciliationEntry
            {
                MessageSid = local.MessageSid,
                To = null,
                ProviderStatus = null,
                ProviderDateSent = null,
                Match = "LocalOnly",
                NotificationId = local.Id,
                LocalStatus = local.Status
            });
        }

        var response = new ReconciliationResponse
        {
            From = fromUtc,
            To = toUtc,
            TotalProviderMessages = providerMessages.Count,
            TotalLocalNotifications = localNotifications.Count,
            Matched = entries.Count(e => e.Match == "Matched"),
            ProviderOnly = entries.Count(e => e.Match == "ProviderOnly"),
            LocalOnly = entries.Count(e => e.Match == "LocalOnly"),
            Entries = entries.OrderBy(e => e.ProviderDateSent ?? DateTimeOffset.MaxValue).ToList()
        };

        return Results.Ok(response);
    }
}

public class ReconciliationRequest : BaseRequest
{
    public ReconciliationRequest(string? from, string? to)
    {
        From = from;
        To = to;
    }

    public string? From { get; }
    public string? To { get; }
}

public class ReconciliationEntry
{
    public string? MessageSid { get; set; }
    public string? To { get; set; }
    public string? ProviderStatus { get; set; }
    public DateTimeOffset? ProviderDateSent { get; set; }
    public string Match { get; set; } = string.Empty;
    public int? NotificationId { get; set; }
    public string? LocalStatus { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int TotalProviderMessages { get; set; }
    public int TotalLocalNotifications { get; set; }
    public int Matched { get; set; }
    public int ProviderOnly { get; set; }
    public int LocalOnly { get; set; }
    public List<ReconciliationEntry> Entries { get; set; } = new();
}

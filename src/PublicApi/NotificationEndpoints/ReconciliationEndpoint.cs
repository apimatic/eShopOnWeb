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

/// <summary>
/// Reconciliation report (operator): lines up the provider's own record of messages sent
/// from this application's sending number in a date range against what eShop believes it
/// sent, so a message known to only one side is visible.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, DateTimeOffset, DateTimeOffset>
{
    private readonly IMessageProvider _messageProvider;
    private readonly IRepository<OrderNotification> _notificationRepository;

    public ReconciliationEndpoint(IMessageProvider messageProvider, IRepository<OrderNotification> notificationRepository)
    {
        _messageProvider = messageProvider;
        _notificationRepository = notificationRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to) =>
            {
                return await HandleAsync(from, to);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(DateTimeOffset from, DateTimeOffset to)
    {
        if (to < from)
        {
            return Results.BadRequest("'to' must not be earlier than 'from'.");
        }

        // The provider is asked for this application's sending number's messages only —
        // the account carries other traffic, and that must not leak into this report.
        var providerMessages = await _messageProvider.ListMessagesAsync(from, to);
        var ourNotifications = await _notificationRepository.ListAsync(new OrderNotificationsInRangeSpecification(from, to));

        var oursBySid = ourNotifications
            .Where(n => n.ProviderMessageSid is not null)
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();

        foreach (var providerMessage in providerMessages)
        {
            var entry = new ReconciliationEntry
            {
                ProviderMessageSid = providerMessage.Sid,
                ProviderStatus = providerMessage.Status,
                ProviderErrorCode = providerMessage.ErrorCode,
                ProviderDateSent = providerMessage.DateSent
            };

            if (providerMessage.Sid is not null && oursBySid.TryGetValue(providerMessage.Sid, out var ours))
            {
                entry.NotificationId = ours.Id;
                entry.OrderId = ours.OrderId;
                entry.Type = ours.Type.ToString();
                entry.OurStatus = ours.ProviderStatus;
                matched.Add(entry);
            }
            else
            {
                providerOnly.Add(entry);
            }
        }

        var providerSids = new HashSet<string>(providerMessages.Where(m => m.Sid is not null).Select(m => m.Sid!));
        var eshopOnly = ourNotifications
            .Where(n => n.ProviderMessageSid is null || !providerSids.Contains(n.ProviderMessageSid))
            .Select(n => new ReconciliationEntry
            {
                NotificationId = n.Id,
                OrderId = n.OrderId,
                Type = n.Type.ToString(),
                ProviderMessageSid = n.ProviderMessageSid,
                OurStatus = n.SendFailed ? "send-failed" : n.ProviderStatus
            })
            .ToList();

        var response = new ReconciliationResponse
        {
            From = from,
            To = to,
            Matched = matched,
            ProviderOnly = providerOnly,
            EshopOnly = eshopOnly
        };
        return Results.Ok(response);
    }
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationEntry> Matched { get; set; } = new List<ReconciliationEntry>();
    public List<ReconciliationEntry> ProviderOnly { get; set; } = new List<ReconciliationEntry>();
    public List<ReconciliationEntry> EshopOnly { get; set; } = new List<ReconciliationEntry>();
}

public class ReconciliationEntry
{
    public string? ProviderMessageSid { get; set; }
    public int? NotificationId { get; set; }
    public int? OrderId { get; set; }
    public string? Type { get; set; }
    public string? ProviderStatus { get; set; }
    public int? ProviderErrorCode { get; set; }
    public DateTimeOffset? ProviderDateSent { get; set; }
    public string? OurStatus { get; set; }
}

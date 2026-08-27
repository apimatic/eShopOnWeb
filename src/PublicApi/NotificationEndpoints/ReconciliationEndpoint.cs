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
/// Reconciliation report (operator action): the provider's own record of messages sent from
/// this application's configured sending number over a date range, lined up against what
/// eShop believes it sent. Covers the whole range.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, DateTime, DateTime>
{
    private readonly ISmsProvider _smsProvider;
    private readonly IRepository<OrderNotification> _notificationRepository;

    public ReconciliationEndpoint(ISmsProvider smsProvider,
        IRepository<OrderNotification> notificationRepository)
    {
        _smsProvider = smsProvider;
        _notificationRepository = notificationRepository;
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
        if (to < from)
        {
            return Results.BadRequest(new ReconciliationResponse { Error = "'to' must not be earlier than 'from'." });
        }

        var fromOffset = new DateTimeOffset(from, TimeSpan.Zero);
        var toOffset = new DateTimeOffset(to, TimeSpan.Zero);

        var providerMessages = await _smsProvider.ListMessagesAsync(fromOffset, toOffset);
        var localNotifications = await _notificationRepository.ListAsync(new NotificationsInRangeSpecification(fromOffset, toOffset));

        var localBySid = localNotifications
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciliationMatch>();
        var providerOnly = new List<ReconciliationProviderMessage>();

        foreach (var message in providerMessages)
        {
            if (localBySid.TryGetValue(message.ProviderMessageSid, out var local))
            {
                matched.Add(new ReconciliationMatch
                {
                    ProviderMessageSid = message.ProviderMessageSid,
                    NotificationId = local.Id,
                    OrderId = local.OrderId,
                    ProviderStatus = message.Status,
                    LocalStatus = local.Status,
                    StatusMatch = string.Equals(message.Status, local.Status, StringComparison.OrdinalIgnoreCase)
                });
            }
            else
            {
                providerOnly.Add(new ReconciliationProviderMessage
                {
                    ProviderMessageSid = message.ProviderMessageSid,
                    Status = message.Status,
                    DateSent = message.DateSent,
                    DateCreated = message.DateCreated
                });
            }
        }

        var providerSids = new HashSet<string>(providerMessages.Select(m => m.ProviderMessageSid));
        var localOnly = localNotifications
            .Where(n => string.IsNullOrEmpty(n.ProviderMessageSid) || !providerSids.Contains(n.ProviderMessageSid))
            .Select(n => new ReconciliationLocalMessage
            {
                NotificationId = n.Id,
                OrderId = n.OrderId,
                Type = n.Type.ToString(),
                Status = n.Status,
                ProviderMessageSid = n.ProviderMessageSid,
                CreatedAt = n.CreatedAt
            })
            .ToList();

        return Results.Ok(new ReconciliationResponse
        {
            From = fromOffset,
            To = toOffset,
            ProviderMessageCount = providerMessages.Count,
            LocalNotificationCount = localNotifications.Count,
            Matched = matched,
            ProviderOnly = providerOnly,
            LocalOnly = localOnly
        });
    }
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int ProviderMessageCount { get; set; }
    public int LocalNotificationCount { get; set; }
    public List<ReconciliationMatch> Matched { get; set; } = new();
    public List<ReconciliationProviderMessage> ProviderOnly { get; set; } = new();
    public List<ReconciliationLocalMessage> LocalOnly { get; set; } = new();
    public string? Error { get; set; }
}

public class ReconciliationMatch
{
    public string ProviderMessageSid { get; set; } = string.Empty;
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string ProviderStatus { get; set; } = string.Empty;
    public string LocalStatus { get; set; } = string.Empty;
    public bool StatusMatch { get; set; }
}

public class ReconciliationProviderMessage
{
    public string ProviderMessageSid { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset? DateSent { get; set; }
    public DateTimeOffset? DateCreated { get; set; }
}

public class ReconciliationLocalMessage
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

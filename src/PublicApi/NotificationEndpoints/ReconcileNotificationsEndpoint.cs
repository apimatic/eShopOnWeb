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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: lines the provider's own record of messages for a date range
/// (only traffic from this application's sending number, asked for server-side)
/// up against what eShop believes it sent.
/// </summary>
public class ReconcileNotificationsEndpoint : IEndpoint<IResult, ReconcileNotificationsRequest, HttpContext>
{
    private readonly ISmsProvider _smsProvider;
    private readonly IRepository<OrderNotification> _notifications;

    public ReconcileNotificationsEndpoint(ISmsProvider smsProvider, IRepository<OrderNotification> notifications)
    {
        _smsProvider = smsProvider;
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string from, string to, HttpContext httpContext) =>
            {
                return await HandleAsync(new ReconcileNotificationsRequest(from, to), httpContext);
            })
            .Produces<ReconcileNotificationsResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconcileNotificationsRequest request, HttpContext httpContext)
    {
        if (!DateTimeOffset.TryParse(request.From, out var from) || !DateTimeOffset.TryParse(request.To, out var to))
        {
            return Results.BadRequest(new { message = "from and to must be ISO-8601 date-times." });
        }
        if (from >= to)
        {
            return Results.BadRequest(new { message = "from must be earlier than to." });
        }

        IReadOnlyList<ProviderMessageRecord> providerRecords;
        try
        {
            providerRecords = await _smsProvider.ListMessagesAsync(from, to, httpContext.RequestAborted);
        }
        catch (SmsProviderException ex)
        {
            return ProviderErrorResults.Map(ex);
        }

        var localNotifications = await _notifications.ListAsync(new NotificationsInRangeSpecification(from, to), httpContext.RequestAborted);

        var localBySid = localNotifications
            .Where(n => n.ProviderMessageSid is not null)
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var response = new ReconcileNotificationsResponse(request.CorrelationId())
        {
            From = from,
            To = to,
            ProviderMessageCount = providerRecords.Count,
            LocalNotificationCount = localNotifications.Count
        };

        var matchedSids = new HashSet<string>();
        foreach (var record in providerRecords)
        {
            if (localBySid.TryGetValue(record.MessageSid, out var local))
            {
                matchedSids.Add(record.MessageSid);
                response.Matched.Add(new ReconciledMessage
                {
                    NotificationId = local.Id,
                    MessageSid = record.MessageSid,
                    LocalStatus = local.Status,
                    ProviderStatus = record.Status,
                    StatusMatch = string.Equals(local.Status, record.Status, StringComparison.OrdinalIgnoreCase),
                    ProviderErrorCode = record.ErrorCode,
                    DateSent = record.DateSent
                });
            }
            else
            {
                // The provider knows about it; eShop doesn't.
                response.ProviderOnly.Add(new ProviderOnlyMessage
                {
                    MessageSid = record.MessageSid,
                    To = record.To,
                    Status = record.Status,
                    DateSent = record.DateSent
                });
            }
        }

        foreach (var local in localNotifications)
        {
            if (local.ProviderMessageSid is null)
            {
                // Never accepted by the provider (the send itself failed locally).
                response.LocalOnly.Add(new LocalOnlyMessage
                {
                    NotificationId = local.Id,
                    OrderId = local.OrderId,
                    Kind = local.Kind.ToString(),
                    Status = local.Status,
                    MessageSid = null,
                    CreatedAt = local.CreatedAt
                });
            }
            else if (!matchedSids.Contains(local.ProviderMessageSid))
            {
                // eShop believes it sent it; the provider has no record in range.
                response.LocalOnly.Add(new LocalOnlyMessage
                {
                    NotificationId = local.Id,
                    OrderId = local.OrderId,
                    Kind = local.Kind.ToString(),
                    Status = local.Status,
                    MessageSid = local.ProviderMessageSid,
                    CreatedAt = local.CreatedAt
                });
            }
        }

        response.MatchedCount = response.Matched.Count;
        return Results.Ok(response);
    }
}

public class ReconcileNotificationsRequest : BaseRequest
{
    public ReconcileNotificationsRequest(string from, string to)
    {
        From = from;
        To = to;
    }

    public string From { get; }
    public string To { get; }
}

public class ReconcileNotificationsResponse : BaseResponse
{
    public ReconcileNotificationsResponse(Guid correlationId) : base(correlationId) { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int ProviderMessageCount { get; set; }
    public int LocalNotificationCount { get; set; }
    public int MatchedCount { get; set; }
    public List<ReconciledMessage> Matched { get; } = new();
    public List<ProviderOnlyMessage> ProviderOnly { get; } = new();
    public List<LocalOnlyMessage> LocalOnly { get; } = new();
}

public class ReconciledMessage
{
    public int NotificationId { get; set; }
    public string MessageSid { get; set; } = string.Empty;
    public string LocalStatus { get; set; } = string.Empty;
    public string ProviderStatus { get; set; } = string.Empty;
    public bool StatusMatch { get; set; }
    public int? ProviderErrorCode { get; set; }
    public DateTimeOffset? DateSent { get; set; }
}

public class ProviderOnlyMessage
{
    public string MessageSid { get; set; } = string.Empty;
    public string? To { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset? DateSent { get; set; }
}

public class LocalOnlyMessage
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? MessageSid { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

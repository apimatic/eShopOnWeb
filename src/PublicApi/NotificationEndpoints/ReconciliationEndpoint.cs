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
/// Operator action: lines up the provider's own record of messages for a date range
/// against what eShop believes it sent. Only messages sent from this application's
/// configured sending number are counted — the provider is asked for that number's
/// messages directly. Covers the whole range (all provider pages).
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
            (DateTimeOffset? from, DateTimeOffset? to) =>
            {
                return await HandleAsync(new ReconciliationRequest(from, to));
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request)
    {
        if (request.From is null || request.To is null)
        {
            return Results.BadRequest(new { message = "Both 'from' and 'to' query parameters are required (ISO-8601 date-times)." });
        }
        if (request.From > request.To)
        {
            return Results.BadRequest(new { message = "'from' must not be later than 'to'." });
        }

        var fromUtc = request.From.Value.ToUniversalTime();
        var toUtc = request.To.Value.ToUniversalTime();

        var providerMessages = await _smsGateway.ListSentAsync(fromUtc, toUtc);
        var localNotifications = await _notificationRepository.ListAsync(
            new OrderNotificationsInRangeSpecification(fromUtc, toUtc));

        var localByProviderSid = localNotifications
            .Where(n => n.ProviderMessageSid is not null)
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());
        var providerSids = new HashSet<string>(providerMessages.Select(m => m.Sid));

        var response = new ReconciliationResponse
        {
            From = fromUtc,
            To = toUtc
        };

        foreach (var message in providerMessages.OrderBy(m => m.DateCreatedUtc))
        {
            localByProviderSid.TryGetValue(message.Sid, out var local);
            response.Entries.Add(new ReconciliationEntry
            {
                Match = local is null ? "providerOnly" : "matched",
                ProviderMessageSid = message.Sid,
                NotificationId = local?.Id,
                OrderId = local?.OrderId,
                Type = local?.Type.ToString(),
                To = message.To,
                ProviderStatus = message.Status,
                LocalStatus = local?.Status,
                ProviderErrorCode = message.ErrorCode,
                DateSentUtc = message.DateSentUtc,
                CreatedUtc = local?.CreatedUtc
            });
        }

        foreach (var notification in localNotifications)
        {
            if (notification.ProviderMessageSid is null || !providerSids.Contains(notification.ProviderMessageSid))
            {
                response.Entries.Add(new ReconciliationEntry
                {
                    Match = "localOnly",
                    ProviderMessageSid = notification.ProviderMessageSid,
                    NotificationId = notification.Id,
                    OrderId = notification.OrderId,
                    Type = notification.Type.ToString(),
                    LocalStatus = notification.Status,
                    ProviderErrorCode = notification.ErrorCode,
                    CreatedUtc = notification.CreatedUtc
                });
            }
        }

        response.Summary = new ReconciliationSummary
        {
            ProviderCount = providerMessages.Count,
            LocalCount = localNotifications.Count,
            MatchedCount = response.Entries.Count(e => e.Match == "matched"),
            ProviderOnlyCount = response.Entries.Count(e => e.Match == "providerOnly"),
            LocalOnlyCount = response.Entries.Count(e => e.Match == "localOnly")
        };

        return Results.Ok(response);
    }
}

public class ReconciliationRequest : BaseRequest
{
    public ReconciliationRequest(DateTimeOffset? from, DateTimeOffset? to)
    {
        From = from;
        To = to;
    }

    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public ReconciliationSummary Summary { get; set; } = new();
    public List<ReconciliationEntry> Entries { get; set; } = new();
}

public class ReconciliationSummary
{
    public int ProviderCount { get; set; }
    public int LocalCount { get; set; }
    public int MatchedCount { get; set; }
    public int ProviderOnlyCount { get; set; }
    public int LocalOnlyCount { get; set; }
}

public class ReconciliationEntry
{
    /// <summary>matched | providerOnly | localOnly</summary>
    public string Match { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
    public int? NotificationId { get; set; }
    public int? OrderId { get; set; }
    public string? Type { get; set; }
    public string? To { get; set; }
    public string? ProviderStatus { get; set; }
    public string? LocalStatus { get; set; }
    public int? ProviderErrorCode { get; set; }
    public DateTimeOffset? DateSentUtc { get; set; }
    public DateTimeOffset? CreatedUtc { get; set; }
}

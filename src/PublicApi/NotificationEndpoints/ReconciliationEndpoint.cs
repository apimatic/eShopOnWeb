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
/// Operator action: lines the provider's own record of messages for a date range up against
/// what eShop believes it sent. Only messages from this application's configured sending
/// number are asked for — the provider account carries other traffic.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IRepository<OrderNotification>>
{
    private readonly INotificationGateway _gateway;

    public ReconciliationEndpoint(INotificationGateway gateway)
    {
        _gateway = gateway;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IRepository<OrderNotification> notificationRepository) =>
            {
                return await HandleAsync(new ReconciliationRequest(from, to), notificationRepository);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IRepository<OrderNotification> notificationRepository)
    {
        if (request.To < request.From)
        {
            return Results.BadRequest(new { message = "'to' must not be earlier than 'from'." });
        }

        IReadOnlyList<ProviderMessage> providerMessages;
        try
        {
            providerMessages = await _gateway.ListMessagesAsync(request.From.ToUniversalTime(), request.To.ToUniversalTime());
        }
        catch (NotificationProviderException ex)
        {
            return Results.Json(new { message = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
        }

        var localNotifications = await notificationRepository.ListAsync(
            new NotificationsInRangeSpecification(request.From.ToUniversalTime(), request.To.ToUniversalTime()));

        var localBySid = localNotifications
            .Where(n => n.MessageSid is not null)
            .GroupBy(n => n.MessageSid!)
            .ToDictionary(g => g.Key, g => g.First());
        var providerSids = providerMessages.Select(m => m.Sid).ToHashSet();

        var response = new ReconciliationResponse(request.CorrelationId())
        {
            From = request.From,
            To = request.To
        };

        foreach (var message in providerMessages)
        {
            var matched = localBySid.TryGetValue(message.Sid, out var local);
            response.Entries.Add(new ReconciliationEntry
            {
                MessageSid = message.Sid,
                Status = message.Status,
                DateSent = message.DateSent,
                Source = matched ? "matched" : "providerOnly",
                NotificationId = matched ? local!.Id : null,
                OrderId = matched ? local!.OrderId : null,
                LocalStatus = matched ? local!.Status : null
            });
        }

        foreach (var notification in localNotifications.Where(n => n.MessageSid is null || !providerSids.Contains(n.MessageSid)))
        {
            response.Entries.Add(new ReconciliationEntry
            {
                MessageSid = notification.MessageSid,
                Status = null,
                DateSent = notification.CreatedAt,
                Source = "localOnly",
                NotificationId = notification.Id,
                OrderId = notification.OrderId,
                LocalStatus = notification.Status
            });
        }

        response.ProviderCount = providerMessages.Count;
        response.LocalCount = localNotifications.Count;
        response.MatchedCount = response.Entries.Count(e => e.Source == "matched");
        response.DiscrepancyCount = response.Entries.Count(e => e.Source != "matched");

        return Results.Ok(response);
    }
}

public class ReconciliationRequest : BaseRequest
{
    public ReconciliationRequest(DateTimeOffset from, DateTimeOffset to)
    {
        From = from;
        To = to;
    }

    public DateTimeOffset From { get; }
    public DateTimeOffset To { get; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) {}
    public ReconciliationResponse() {}

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int ProviderCount { get; set; }
    public int LocalCount { get; set; }
    public int MatchedCount { get; set; }
    public int DiscrepancyCount { get; set; }
    public List<ReconciliationEntry> Entries { get; set; } = new();
}

public class ReconciliationEntry
{
    public string? MessageSid { get; set; }
    public string Source { get; set; } = string.Empty;
    public string? Status { get; set; }
    public string? LocalStatus { get; set; }
    public DateTimeOffset? DateSent { get; set; }
    public int? NotificationId { get; set; }
    public int? OrderId { get; set; }
}

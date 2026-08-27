using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: lines the provider's own record of messages sent from the shop's sending
/// number in a date range up against what eShop believes it sent.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IRepository<OrderNotification>, ITextMessagingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to, IRepository<OrderNotification> notificationRepository, ITextMessagingService messagingService) =>
            {
                return await HandleAsync(new ReconciliationRequest(from, to), notificationRepository, messagingService);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IRepository<OrderNotification> notificationRepository, ITextMessagingService messagingService)
    {
        if (!DateTimeOffset.TryParse(request.From, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var from)
            || !DateTimeOffset.TryParse(request.To, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var to))
        {
            return Results.BadRequest(new { message = "from and to are required and must be ISO-8601 date-times." });
        }
        if (to < from)
        {
            return Results.BadRequest(new { message = "to must not be earlier than from." });
        }

        IReadOnlyList<ProviderTextMessage> providerMessages;
        try
        {
            providerMessages = await messagingService.ListSentMessagesAsync(from, to);
        }
        catch (TextMessagingException)
        {
            return Results.Problem("The messaging provider could not be reached for reconciliation.", statusCode: 502);
        }

        var eshopNotifications = await notificationRepository.ListAsync(new OrderNotificationsInRangeSpecification(from, to));

        var eshopBySid = eshopNotifications
            .Where(n => n.MessageSid is not null)
            .GroupBy(n => n.MessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var response = new ReconciliationResponse(request.CorrelationId())
        {
            From = from,
            To = to,
            TotalProviderMessages = providerMessages.Count,
            TotalEshopNotifications = eshopNotifications.Count
        };

        foreach (var providerMessage in providerMessages)
        {
            eshopBySid.TryGetValue(providerMessage.Sid, out var match);
            response.Entries.Add(new ReconciliationEntryDto
            {
                MessageSid = providerMessage.Sid,
                To = providerMessage.To,
                ProviderStatus = providerMessage.Status,
                DateSent = providerMessage.DateSent,
                ErrorCode = providerMessage.ErrorCode,
                NotificationId = match?.Id,
                EshopStatus = match?.Status,
                Match = match is null ? "providerOnly" : "matched"
            });
        }

        var providerSids = new HashSet<string>(providerMessages.Select(m => m.Sid));
        foreach (var notification in eshopNotifications.Where(n => n.MessageSid is null || !providerSids.Contains(n.MessageSid)))
        {
            response.Entries.Add(new ReconciliationEntryDto
            {
                MessageSid = notification.MessageSid,
                NotificationId = notification.Id,
                Kind = notification.Kind.ToString(),
                EshopStatus = notification.Status,
                CreatedAt = notification.CreatedAt,
                Match = "eShopOnly"
            });
        }

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

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }
    public ReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int TotalProviderMessages { get; set; }
    public int TotalEshopNotifications { get; set; }
    public List<ReconciliationEntryDto> Entries { get; set; } = new();
}

public class ReconciliationEntryDto
{
    public string? MessageSid { get; set; }
    public int? NotificationId { get; set; }
    public string? Kind { get; set; }
    public string? To { get; set; }
    public string? ProviderStatus { get; set; }
    public string? EshopStatus { get; set; }
    public DateTimeOffset? DateSent { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public int? ErrorCode { get; set; }

    /// <summary>matched | providerOnly | eShopOnly</summary>
    public string Match { get; set; } = string.Empty;
}

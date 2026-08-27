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
/// Reconciliation report (operator action): lines up the provider's own record
/// of messages sent from this application's configured sending number against
/// what eShop believes it sent, over a date range. Messages known on only one
/// side are surfaced.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest>
{
    private readonly ISmsService _smsService;
    private readonly IRepository<OrderNotification> _notificationRepository;

    public ReconciliationEndpoint(ISmsService smsService,
        IRepository<OrderNotification> notificationRepository)
    {
        _smsService = smsService;
        _notificationRepository = notificationRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to });
            })
            .Produces<ReconciliationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request)
    {
        var response = new ReconciliationResponse(request.CorrelationId())
        {
            From = request.From,
            To = request.To
        };

        if (request.To < request.From)
        {
            response.Error = "'to' must not be earlier than 'from'.";
            return Results.BadRequest(response);
        }

        // The provider is asked for this application's sending number's messages
        // only; traffic belonging to other applications never enters the report.
        var providerMessages = await _smsService.ListMessagesAsync(request.From, request.To);

        var localSpec = new OrderNotificationsInRangeSpecification(request.From, request.To);
        var localNotifications = await _notificationRepository.ListAsync(localSpec);

        var localBySid = localNotifications
            .Where(n => n.MessageSid is not null)
            .GroupBy(n => n.MessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var message in providerMessages)
        {
            OrderNotification? local = null;
            var matched = message.MessageSid is not null && localBySid.TryGetValue(message.MessageSid, out local);
            response.Entries.Add(new ReconciliationEntry
            {
                MessageSid = message.MessageSid,
                NotificationId = local?.Id,
                ProviderStatus = message.Status,
                LocalStatus = local?.Status,
                DateSent = message.DateSent,
                Discrepancy = !matched || local is null
                    ? "MissingLocally"
                    : (string.Equals(message.Status, local.Status, StringComparison.OrdinalIgnoreCase) ? null : "StatusMismatch")
            });
        }

        var providerSids = providerMessages.Select(m => m.MessageSid).ToHashSet();
        foreach (var notification in localNotifications)
        {
            if (notification.MessageSid is null)
            {
                response.Entries.Add(new ReconciliationEntry
                {
                    MessageSid = null,
                    NotificationId = notification.Id,
                    ProviderStatus = null,
                    LocalStatus = notification.Status,
                    Discrepancy = "MissingAtProvider"
                });
                continue;
            }

            if (providerSids.Contains(notification.MessageSid))
            {
                continue;
            }

            // The provider's date filter keys off date_sent, which scheduled or
            // in-flight messages do not have yet. Fetch those directly before
            // calling them missing.
            var fetched = await _smsService.GetMessageAsync(notification.MessageSid);
            response.Entries.Add(new ReconciliationEntry
            {
                MessageSid = notification.MessageSid,
                NotificationId = notification.Id,
                ProviderStatus = fetched?.Status,
                LocalStatus = notification.Status,
                DateSent = fetched?.DateSent,
                Discrepancy = fetched is null
                    ? "MissingAtProvider"
                    : (string.Equals(fetched.Status, notification.Status, StringComparison.OrdinalIgnoreCase) ? null : "StatusMismatch")
            });
        }

        response.ProviderMessageCount = providerMessages.Count;
        response.LocalNotificationCount = localNotifications.Count;
        return Results.Ok(response);
    }
}

public class ReconciliationRequest : BaseRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }
    public ReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int ProviderMessageCount { get; set; }
    public int LocalNotificationCount { get; set; }
    public List<ReconciliationEntry> Entries { get; set; } = new();
    public string? Error { get; set; }
}

public class ReconciliationEntry
{
    public string? MessageSid { get; set; }
    public int? NotificationId { get; set; }
    public string? ProviderStatus { get; set; }
    public string? LocalStatus { get; set; }
    public DateTimeOffset? DateSent { get; set; }

    /// <summary>Null when both sides agree; otherwise MissingLocally, MissingAtProvider or StatusMismatch.</summary>
    public string? Discrepancy { get; set; }
}

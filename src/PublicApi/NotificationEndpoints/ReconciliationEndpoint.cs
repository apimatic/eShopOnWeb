using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.Twilio;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Reconciliation report (operator): the provider's own record of messages sent from this
/// application's configured sending number over a date range, lined up against eShop's
/// notification records. The sender filter is applied by the provider, not after the fact.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IRepository<OrderNotification>>
{
    private readonly ISmsService _smsService;
    private readonly TwilioSettings _twilioSettings;

    public ReconciliationEndpoint(ISmsService smsService, IOptions<TwilioSettings> twilioSettings)
    {
        _smsService = smsService;
        _twilioSettings = twilioSettings.Value;
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
            return Results.BadRequest("'to' must not be earlier than 'from'.");
        }

        var providerMessages = await _smsService.ListMessagesAsync(request.From, request.To);
        var localNotifications = await notificationRepository.ListAsync(
            new NotificationsCreatedInRangeSpecification(request.From, request.To));

        var localBySid = localNotifications
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var response = new ReconciliationResponse(request.CorrelationId())
        {
            From = request.From,
            To = request.To,
            FromNumber = _twilioSettings.FromNumber ?? string.Empty,
            ProviderMessageCount = providerMessages.Count,
            LocalNotificationCount = localNotifications.Count
        };

        foreach (var message in providerMessages)
        {
            var matched = localBySid.TryGetValue(message.Sid, out var notification);
            var entry = new ReconciliationEntryDto
            {
                MessageSid = message.Sid,
                To = message.To,
                Status = message.Status,
                DateSent = message.DateSent,
                NotificationId = matched ? notification!.Id : null
            };
            response.Entries.Add(entry);
            if (!matched)
            {
                response.ProviderOnly.Add(entry);
            }
        }

        var providerSids = providerMessages.Select(m => m.Sid).ToHashSet();
        response.EShopOnly = localNotifications
            .Where(n => string.IsNullOrEmpty(n.ProviderMessageSid) || !providerSids.Contains(n.ProviderMessageSid!))
            .Select(n => new EShopOnlyEntryDto
            {
                NotificationId = n.Id,
                MessageSid = n.ProviderMessageSid,
                Status = n.Status,
                CreatedOn = n.CreatedOn
            })
            .ToList();

        response.MatchedCount = response.Entries.Count(e => e.NotificationId.HasValue);

        return Results.Ok(response);
    }
}

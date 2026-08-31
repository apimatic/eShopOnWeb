using System;
using System.Collections.Generic;
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
using Microsoft.eShopWeb.Infrastructure.Messaging;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }

    /// <summary>This application's configured sending number the report covers.</summary>
    public string FromNumber { get; set; } = string.Empty;

    public int ProviderCount { get; set; }
    public int LocalCount { get; set; }
    public int MatchedCount { get; set; }

    /// <summary>Messages both sides know about, with each side's view of the outcome.</summary>
    public List<ReconciledMessageDto> Matched { get; set; } = new List<ReconciledMessageDto>();

    /// <summary>Messages the provider knows about from our number that eShop has no record of.</summary>
    public List<ReconciledMessageDto> ProviderOnly { get; set; } = new List<ReconciledMessageDto>();

    /// <summary>Messages eShop believes it sent that the provider has no record of in range.</summary>
    public List<ReconciledMessageDto> LocalOnly { get; set; } = new List<ReconciledMessageDto>();
}

public class ReconciledMessageDto
{
    public string? ProviderMessageSid { get; set; }
    public int? NotificationId { get; set; }
    public int? OrderId { get; set; }
    public string? Type { get; set; }
    public string? ProviderStatus { get; set; }
    public string? LocalStatus { get; set; }
    public DateTimeOffset? DateSent { get; set; }
}

/// <summary>
/// Lines up the provider's own record of messages sent from this
/// application's configured sending number against what eShop believes it
/// sent, over the given ISO-8601 date-time range (operator).
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, object, IMessageProvider>
{
    private readonly IReadRepository<OrderNotification> _notificationRepository;
    private readonly TwilioSettings _settings;

    public ReconciliationEndpoint(IReadRepository<OrderNotification> notificationRepository, IOptions<TwilioSettings> settings)
    {
        _notificationRepository = notificationRepository;
        _settings = settings.Value;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string from, string to, IMessageProvider messageProvider) =>
            {
                return await HandleAsync(from, to, messageProvider);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(object request, IMessageProvider messageProvider)
    {
        throw new NotSupportedException();
    }

    private async Task<IResult> HandleAsync(string from, string to, IMessageProvider messageProvider)
    {
        if (!DateTimeOffset.TryParse(from, null, System.Globalization.DateTimeStyles.RoundtripKind, out var fromDate) ||
            !DateTimeOffset.TryParse(to, null, System.Globalization.DateTimeStyles.RoundtripKind, out var toDate))
        {
            return Results.BadRequest("'from' and 'to' must be ISO-8601 date-times.");
        }
        if (toDate < fromDate)
        {
            return Results.BadRequest("'to' must not be earlier than 'from'.");
        }

        // The provider is asked for our configured sending number's messages only.
        var providerMessages = await messageProvider.ListMessagesAsync(fromDate, toDate);
        var localNotifications = await _notificationRepository.ListAsync(
            new OrderNotificationsInRangeSpecification(fromDate, toDate));

        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.MessageSid))
            .GroupBy(m => m.MessageSid)
            .ToDictionary(g => g.Key, g => g.First());
        var localBySid = localNotifications
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var response = new ReconciliationResponse
        {
            From = fromDate,
            To = toDate,
            FromNumber = _settings.FromNumber,
            ProviderCount = providerMessages.Count,
            LocalCount = localNotifications.Count
        };

        foreach (var local in localNotifications)
        {
            var dto = new ReconciledMessageDto
            {
                NotificationId = local.Id,
                OrderId = local.OrderId,
                Type = local.Type.ToString(),
                LocalStatus = local.ProviderStatus,
                ProviderMessageSid = local.ProviderMessageSid
            };

            if (local.ProviderMessageSid is not null && providerBySid.TryGetValue(local.ProviderMessageSid, out var providerMessage))
            {
                dto.ProviderStatus = providerMessage.Status;
                dto.DateSent = providerMessage.DateSent;
                response.Matched.Add(dto);
            }
            else
            {
                // Never accepted by the provider, or outside what the provider reports in range.
                response.LocalOnly.Add(dto);
            }
        }

        foreach (var providerMessage in providerMessages)
        {
            if (!localBySid.ContainsKey(providerMessage.MessageSid))
            {
                response.ProviderOnly.Add(new ReconciledMessageDto
                {
                    ProviderMessageSid = providerMessage.MessageSid,
                    ProviderStatus = providerMessage.Status,
                    DateSent = providerMessage.DateSent
                });
            }
        }

        response.MatchedCount = response.Matched.Count;
        return Results.Ok(response);
    }
}

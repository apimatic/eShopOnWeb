using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconciliationEntry
{
    public string? ProviderMessageSid { get; set; }
    public int? NotificationId { get; set; }
    public string? ProviderStatus { get; set; }
    public string? LocalStatus { get; set; }
    public DateTimeOffset? DateSent { get; set; }
    public int? ProviderErrorCode { get; set; }
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }

    /// <summary>Messages both the provider and eShop know about.</summary>
    public List<ReconciliationEntry> Matched { get; set; } = new();

    /// <summary>Messages the provider recorded from our sending number that eShop has no record of.</summary>
    public List<ReconciliationEntry> ProviderOnly { get; set; } = new();

    /// <summary>Messages eShop believes it sent that the provider has no record of in range.</summary>
    public List<ReconciliationEntry> LocalOnly { get; set; } = new();
}

/// <summary>
/// Lines up the provider's own record of messages sent from this application's
/// configured sending number against what eShop believes it sent, over a date
/// range (operator).
/// </summary>
[Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class ReconciliationEndpoint : EndpointBaseAsync
    .WithRequest<ReconciliationEndpoint.ReconciliationRequest>
    .WithActionResult<ReconciliationResponse>
{
    public class ReconciliationRequest
    {
        [FromQuery, Required]
        public string From { get; set; } = string.Empty;

        [FromQuery, Required]
        public string To { get; set; } = string.Empty;
    }

    private readonly IMessagingClient _messagingClient;
    private readonly IRepository<OrderNotification> _notifications;

    public ReconciliationEndpoint(IMessagingClient messagingClient, IRepository<OrderNotification> notifications)
    {
        _messagingClient = messagingClient;
        _notifications = notifications;
    }

    [HttpGet("api/notifications/reconciliation")]
    [SwaggerOperation(Summary = "Reconciles the provider's message record against eShop's (operator)", Tags = new[] { "NotificationEndpoints" })]
    public override async Task<ActionResult<ReconciliationResponse>> HandleAsync(
        ReconciliationRequest request, CancellationToken cancellationToken = default)
    {
        if (!DateTimeOffset.TryParse(request.From, out var from) || !DateTimeOffset.TryParse(request.To, out var to))
        {
            return BadRequest(new { error = "'from' and 'to' must be ISO-8601 date-times." });
        }
        if (to < from)
        {
            return BadRequest(new { error = "'to' must not be earlier than 'from'." });
        }

        var providerMessages = await _messagingClient.ListMessagesAsync(from, to, cancellationToken);
        var localNotifications = await _notifications.ListAsync(
            new NotificationsInRangeSpecification(from, to), cancellationToken);

        var localBySid = localNotifications
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());
        var providerSids = new HashSet<string>(providerMessages.Select(m => m.Sid));

        var response = new ReconciliationResponse { From = from, To = to };

        foreach (var message in providerMessages)
        {
            if (localBySid.TryGetValue(message.Sid, out var local))
            {
                if (message.Status is not null && message.Status != local.Status)
                {
                    local.UpdateProviderState(message.Status, message.ErrorCode, message.ErrorMessage);
                    await _notifications.UpdateAsync(local, cancellationToken);
                }
                response.Matched.Add(new ReconciliationEntry
                {
                    ProviderMessageSid = message.Sid,
                    NotificationId = local.Id,
                    ProviderStatus = message.Status,
                    LocalStatus = local.Status,
                    DateSent = message.DateSent,
                    ProviderErrorCode = message.ErrorCode
                });
            }
            else
            {
                response.ProviderOnly.Add(new ReconciliationEntry
                {
                    ProviderMessageSid = message.Sid,
                    ProviderStatus = message.Status,
                    DateSent = message.DateSent,
                    ProviderErrorCode = message.ErrorCode
                });
            }
        }

        foreach (var local in localNotifications)
        {
            if (local.ProviderMessageSid is null || !providerSids.Contains(local.ProviderMessageSid))
            {
                response.LocalOnly.Add(new ReconciliationEntry
                {
                    ProviderMessageSid = local.ProviderMessageSid,
                    NotificationId = local.Id,
                    LocalStatus = local.Status,
                    DateSent = null,
                    ProviderErrorCode = local.ProviderErrorCode
                });
            }
        }

        return response;
    }
}

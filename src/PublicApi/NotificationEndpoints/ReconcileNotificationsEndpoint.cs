using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: reconciliation report. Lists the provider's own record of
/// messages sent from this application's configured sending number over a date
/// range and lines them up against what eShop believes it sent.
/// </summary>
[Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class ReconcileNotificationsEndpoint : EndpointBaseAsync
    .WithRequest<ReconcileNotificationsRequest>
    .WithActionResult<ReconcileNotificationsResponse>
{
    private readonly IOrderNotificationService _notificationService;

    public ReconcileNotificationsEndpoint(IOrderNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    [HttpGet("api/notifications/reconciliation")]
    [SwaggerOperation(
        Summary = "Reconciles notifications with the provider",
        Description = "Compares the provider's message records for the shop's sending number against local records",
        OperationId = "notifications.reconcile",
        Tags = new[] { "NotificationEndpoints" })
    ]
    public override async Task<ActionResult<ReconcileNotificationsResponse>> HandleAsync(
        [FromQuery] ReconcileNotificationsRequest request, CancellationToken cancellationToken = default)
    {
        if (!DateTimeOffset.TryParse(request.From, out var from) || !DateTimeOffset.TryParse(request.To, out var to))
        {
            return BadRequest("Parameters 'from' and 'to' must be ISO-8601 date-times.");
        }
        if (to < from)
        {
            return BadRequest("Parameter 'to' must not be earlier than 'from'.");
        }

        try
        {
            var result = await _notificationService.ReconcileAsync(from, to, cancellationToken);
            return new ReconcileNotificationsResponse
            {
                From = from,
                To = to,
                ProviderMessageCount = result.ProviderMessages.Count,
                Matched = result.MatchedMessageSids.Select(sid => new ReconciliationEntry
                {
                    MessageSid = sid,
                    State = "matched"
                }).ToList(),
                MissingLocally = result.MissingLocally.Select(m => new ReconciliationEntry
                {
                    MessageSid = m.MessageSid,
                    State = "missing-locally",
                    ProviderStatus = m.Status,
                    ProviderDateSent = m.DateSent
                }).ToList(),
                MissingAtProvider = result.MissingAtProvider.Select(n => new ReconciliationEntry
                {
                    MessageSid = n.MessageSid!,
                    State = "missing-at-provider",
                    NotificationId = n.Id,
                    LocalStatus = n.Status
                }).ToList()
            };
        }
        catch (SmsProviderException)
        {
            return StatusCode(502, "The messaging provider's records could not be retrieved.");
        }
    }
}

public class ReconcileNotificationsRequest
{
    [FromQuery(Name = "from")]
    public string From { get; set; } = string.Empty;

    [FromQuery(Name = "to")]
    public string To { get; set; } = string.Empty;
}

public class ReconcileNotificationsResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int ProviderMessageCount { get; set; }
    public List<ReconciliationEntry> Matched { get; set; } = new();
    public List<ReconciliationEntry> MissingLocally { get; set; } = new();
    public List<ReconciliationEntry> MissingAtProvider { get; set; } = new();
}

public class ReconciliationEntry
{
    public string MessageSid { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public int? NotificationId { get; set; }
    public string? LocalStatus { get; set; }
    public string? ProviderStatus { get; set; }
    public DateTimeOffset? ProviderDateSent { get; set; }
}

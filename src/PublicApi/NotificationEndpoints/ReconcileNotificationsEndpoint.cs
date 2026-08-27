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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: lines up the provider's own record of messages for a date range against
/// what eShop believes it sent. Only messages sent from this application's configured sending
/// number are counted — the filter is applied by the provider, in the request.
/// </summary>
[Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class ReconcileNotificationsEndpoint : EndpointBaseAsync
    .WithRequest<ReconcileNotificationsRequest>
    .WithActionResult<ReconcileNotificationsResponse>
{
    private readonly INotificationManagementService _notificationManagementService;

    public ReconcileNotificationsEndpoint(INotificationManagementService notificationManagementService)
    {
        _notificationManagementService = notificationManagementService;
    }

    [HttpGet("api/notifications/reconciliation")]
    [SwaggerOperation(
        Summary = "Reconciles provider message records against local notifications (operator)",
        Description = "from and to are ISO-8601 date-times; the report covers the whole range",
        OperationId = "notifications.reconciliation",
        Tags = new[] { "NotificationEndpoints" })
    ]
    public override async Task<ActionResult<ReconcileNotificationsResponse>> HandleAsync(
        ReconcileNotificationsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.To < request.From)
        {
            throw new ArgumentException("The 'to' boundary must not be earlier than 'from'.");
        }

        var report = await _notificationManagementService.ReconcileAsync(request.From, request.To, cancellationToken);

        return new ReconcileNotificationsResponse(request.CorrelationId())
        {
            From = report.From,
            To = report.To,
            MatchedCount = report.MatchedCount,
            ProviderOnlyCount = report.ProviderOnlyCount,
            LocalOnlyCount = report.LocalOnlyCount,
            ProviderListingTruncated = report.ProviderListingTruncated,
            Entries = report.Entries.Select(e => new ReconciliationEntryDto
            {
                MessageSid = e.MessageSid,
                NotificationId = e.NotificationId,
                Match = e.Match,
                ProviderStatus = e.ProviderStatus,
                LocalStatus = e.LocalStatus,
                DateSent = e.DateSent
            }).ToList()
        };
    }
}

public class ReconcileNotificationsRequest : BaseRequest
{
    [Required]
    [FromQuery(Name = "from")]
    public DateTimeOffset From { get; set; }

    [Required]
    [FromQuery(Name = "to")]
    public DateTimeOffset To { get; set; }
}

public class ReconcileNotificationsResponse : BaseResponse
{
    public ReconcileNotificationsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ReconcileNotificationsResponse()
    {
    }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int MatchedCount { get; set; }
    public int ProviderOnlyCount { get; set; }
    public int LocalOnlyCount { get; set; }
    public bool ProviderListingTruncated { get; set; }
    public List<ReconciliationEntryDto> Entries { get; set; } = new();
}

public class ReconciliationEntryDto
{
    public string? MessageSid { get; set; }
    public int? NotificationId { get; set; }

    /// <summary>Matched | ProviderOnly | LocalOnly</summary>
    public string Match { get; set; } = string.Empty;

    public string? ProviderStatus { get; set; }
    public string? LocalStatus { get; set; }
    public DateTimeOffset? DateSent { get; set; }
}

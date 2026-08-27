using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Reconciliation report (operator): the provider's own record of messages sent from
/// this application's sending number over a date range, lined up against what eShop
/// believes it sent.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, DateTimeOffset, DateTimeOffset>
{
    private readonly IOrderNotificationService _notificationService;

    public ReconciliationEndpoint(IOrderNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to) =>
            {
                return await HandleAsync(from, to);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(DateTimeOffset from, DateTimeOffset to)
    {
        if (to < from)
        {
            return Results.BadRequest(new { message = "The 'to' date-time must not be earlier than 'from'." });
        }

        var entries = await _notificationService.ReconcileAsync(from.ToUniversalTime(), to.ToUniversalTime());

        var response = new ReconciliationResponse
        {
            From = from,
            To = to,
            Entries = entries.Select(e => new ReconciliationEntryDto
            {
                ProviderMessageSid = e.ProviderMessageSid,
                ProviderStatus = e.ProviderStatus,
                To = e.To,
                DateSent = e.DateSent,
                DateCreated = e.DateCreated,
                LocalNotificationId = e.LocalNotificationId,
                LocalOrderId = e.LocalOrderId,
                LocalStatus = e.LocalStatus,
                Disposition = e.Disposition
            }).ToList()
        };

        return Results.Ok(response);
    }
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationEntryDto> Entries { get; set; } = new();
}

public class ReconciliationEntryDto
{
    public string ProviderMessageSid { get; set; } = string.Empty;
    public string? ProviderStatus { get; set; }
    public string? To { get; set; }
    public DateTimeOffset? DateSent { get; set; }
    public DateTimeOffset? DateCreated { get; set; }
    public int? LocalNotificationId { get; set; }
    public int? LocalOrderId { get; set; }
    public string? LocalStatus { get; set; }

    /// <summary>Matched | MissingLocally (provider knows it, eShop doesn't) | MissingAtProvider (eShop sent it, provider has no record in range).</summary>
    public string Disposition { get; set; } = string.Empty;
}

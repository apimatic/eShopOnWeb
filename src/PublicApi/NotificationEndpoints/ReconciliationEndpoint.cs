using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconciliationEntryDto
{
    public int? NotificationId { get; set; }
    public string? ProviderMessageSid { get; set; }
    public string? LocalStatus { get; set; }
    public string? ProviderStatus { get; set; }
    public DateTimeOffset? ProviderDateSent { get; set; }
    public bool StatusMatches { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int ProviderMessageCount { get; set; }
    public int LocalNotificationCount { get; set; }
    public List<ReconciliationEntryDto> Matched { get; set; } = new();
    public List<ReconciliationEntryDto> MissingFromEShop { get; set; } = new();
    public List<ReconciliationEntryDto> MissingFromProvider { get; set; } = new();
}

/// <summary>
/// Reconciliation report (operator action): the provider's own record of messages
/// sent from this application's sending number over a date range, lined up against
/// what eShop believes it sent.
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
        if (from == default || to == default || to < from)
        {
            return Results.BadRequest(new { message = "Both 'from' and 'to' ISO-8601 date-times are required, and 'to' must not precede 'from'." });
        }

        var report = await _notificationService.ReconcileAsync(from, to);

        static ReconciliationEntryDto ToDto(ReconciliationEntry e) => new()
        {
            NotificationId = e.NotificationId,
            ProviderMessageSid = e.ProviderMessageSid,
            LocalStatus = e.LocalStatus,
            ProviderStatus = e.ProviderStatus,
            ProviderDateSent = e.ProviderDateSent,
            StatusMatches = e.StatusMatches
        };

        return Results.Ok(new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            ProviderMessageCount = report.ProviderMessageCount,
            LocalNotificationCount = report.LocalNotificationCount,
            Matched = report.Matched.ConvertAll(ToDto),
            MissingFromEShop = report.MissingFromEShop.ConvertAll(ToDto),
            MissingFromProvider = report.MissingFromProvider.ConvertAll(ToDto)
        });
    }
}

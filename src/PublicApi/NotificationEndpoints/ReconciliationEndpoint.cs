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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconciliationEntryDto
{
    public string? ProviderMessageSid { get; set; }
    public string? Status { get; set; }
    public int? OrderId { get; set; }
    public string? Kind { get; set; }
    public DateTimeOffset? DateSent { get; set; }
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }

    public int ProviderCount { get; set; }
    public int MatchedCount { get; set; }
    public int ProviderOnlyCount { get; set; }
    public int EShopOnlyCount { get; set; }

    /// <summary>Messages both the provider and eShop know about, lined up.</summary>
    public List<ReconciliationEntryDto> Matched { get; set; } = new();

    /// <summary>Messages the provider knows about (from the configured sender) that eShop does not.</summary>
    public List<ReconciliationEntryDto> ProviderOnly { get; set; } = new();

    /// <summary>Messages eShop believes it sent that the provider's record does not show.</summary>
    public List<ReconciliationEntryDto> EShopOnly { get; set; } = new();
}

/// <summary>
/// Operator report: the provider's own record of messages sent from this application's configured sending
/// number over a date range, lined up against what eShop believes it sent.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, IOrderNotificationService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IOrderNotificationService service, HttpContext http) =>
                await HandleAsync(service, http))
            .Produces<ReconciliationResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(IOrderNotificationService service, HttpContext http)
    {
        var fromRaw = http.Request.Query["from"].ToString();
        var toRaw = http.Request.Query["to"].ToString();

        if (!TryParseIso(fromRaw, out var from) || !TryParseIso(toRaw, out var to))
        {
            return Results.BadRequest(new { message = "Both 'from' and 'to' must be ISO-8601 date-times." });
        }

        if (to < from)
        {
            return Results.BadRequest(new { message = "'to' must not be earlier than 'from'." });
        }

        var report = await service.ReconcileAsync(from, to);

        return Results.Ok(new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            ProviderCount = report.Matched.Count + report.ProviderOnly.Count,
            MatchedCount = report.Matched.Count,
            ProviderOnlyCount = report.ProviderOnly.Count,
            EShopOnlyCount = report.EShopOnly.Count,
            Matched = report.Matched.Select(Map).ToList(),
            ProviderOnly = report.ProviderOnly.Select(Map).ToList(),
            EShopOnly = report.EShopOnly.Select(Map).ToList()
        });
    }

    private static bool TryParseIso(string value, out DateTimeOffset result)
    {
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result);
    }

    private static ReconciliationEntryDto Map(ReconciliationEntry e) => new()
    {
        ProviderMessageSid = e.ProviderMessageSid,
        Status = e.Status,
        OrderId = e.OrderId,
        Kind = e.Kind,
        DateSent = e.DateSent
    };
}

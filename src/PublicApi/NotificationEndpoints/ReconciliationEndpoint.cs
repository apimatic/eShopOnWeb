using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
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
    public int? NotificationId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Destination { get; set; }
    public DateTimeOffset? DateSent { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public int MatchedCount { get; set; }
    public int ProviderOnlyCount { get; set; }
    public int EShopOnlyCount { get; set; }

    /// <summary>Messages present in both the provider's records and eShop's.</summary>
    public List<ReconciliationEntryDto> Matched { get; set; } = new();

    /// <summary>Messages the provider knows about (from our number, in range) that eShop has no record of.</summary>
    public List<ReconciliationEntryDto> ProviderOnly { get; set; } = new();

    /// <summary>Messages eShop believes it sent that the provider's range list does not include.</summary>
    public List<ReconciliationEntryDto> EShopOnly { get; set; } = new();
}

/// <summary>
/// Operator action: reconciles the provider's own record of messages sent from the configured
/// sending number, over a date range, against what eShop believes it sent.
/// </summary>
public class ReconciliationEndpoint : IEndpoint
{
    private readonly IOrderNotificationService _orderNotificationService;

    public ReconciliationEndpoint(IOrderNotificationService orderNotificationService)
    {
        _orderNotificationService = orderNotificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string from, string to, CancellationToken ct) => await HandleAsync(from, to, ct))
            .Produces<ReconciliationResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(string from, string to, CancellationToken ct)
    {
        if (!TryParseIso(from, out var fromDate) || !TryParseIso(to, out var toDate))
            return Results.BadRequest(new { error = "'from' and 'to' must be ISO-8601 date-times." });

        if (toDate < fromDate)
            return Results.BadRequest(new { error = "'to' must not be earlier than 'from'." });

        var report = await _orderNotificationService.ReconcileAsync(fromDate, toDate, ct);

        var response = new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            FromNumber = report.FromNumber,
            MatchedCount = report.Matched.Count,
            ProviderOnlyCount = report.ProviderOnly.Count,
            EShopOnlyCount = report.EShopOnly.Count,
            Matched = report.Matched.Select(Map).ToList(),
            ProviderOnly = report.ProviderOnly.Select(Map).ToList(),
            EShopOnly = report.EShopOnly.Select(Map).ToList()
        };
        return Results.Ok(response);
    }

    private static ReconciliationEntryDto Map(ReconciliationEntry e) => new()
    {
        ProviderMessageSid = e.ProviderMessageSid,
        NotificationId = e.NotificationId,
        Status = e.Status,
        Destination = e.MaskedTo,
        DateSent = e.DateSent
    };

    private static bool TryParseIso(string value, out DateTimeOffset result) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result);
}

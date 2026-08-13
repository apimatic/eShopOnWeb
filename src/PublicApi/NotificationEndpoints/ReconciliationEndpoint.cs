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

/// <summary>
/// GET /api/notifications/reconciliation?from={from}&amp;to={to} — operator action. Lists the
/// provider's own record of messages for the range (asked of the provider for THIS application's
/// configured sending number only) and lines them up against what eShop believes it sent, so a
/// message known to one side and not the other is visible. <c>from</c>/<c>to</c> are ISO-8601.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to, IOrderNotificationService service) =>
            {
                if (!TryParse(from, out var fromDto) || !TryParse(to, out var toDto))
                {
                    return Results.BadRequest(new { message = "'from' and 'to' must be ISO-8601 date-times." });
                }

                var report = await service.ReconcileAsync(fromDto, toDto);
                return Results.Ok(ReconciliationResponse.Create(report));
            })
            .Produces<ReconciliationResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    private static bool TryParse(string? value, out DateTimeOffset result)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result);

    public Task<IResult> HandleAsync(IOrderNotificationService service) => Task.FromResult(Results.Ok());
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int MatchedCount { get; set; }
    public int ProviderOnlyCount { get; set; }
    public int EShopOnlyCount { get; set; }
    public List<ReconciliationMatchDto> Matched { get; set; } = new();
    public List<ReconciliationProviderOnlyDto> OnlyAtProvider { get; set; } = new();
    public List<ReconciliationEShopOnlyDto> OnlyInEShop { get; set; } = new();

    public static ReconciliationResponse Create(ReconciliationReport report) => new()
    {
        From = report.From,
        To = report.To,
        MatchedCount = report.Matched.Count,
        ProviderOnlyCount = report.OnlyAtProvider.Count,
        EShopOnlyCount = report.OnlyInEShop.Count,
        Matched = report.Matched.Select(m => new ReconciliationMatchDto
        {
            NotificationId = m.NotificationId,
            ProviderMessageSid = m.ProviderMessageSid,
            Kind = m.Kind.ToString(),
            OrderId = m.OrderId,
            LocalStatus = m.LocalStatus,
            ProviderStatus = m.ProviderStatus,
            StatusMatches = m.StatusMatches
        }).ToList(),
        // Provider-only rows expose the provider's identifiers/outcome only — never the destination number or content.
        OnlyAtProvider = report.OnlyAtProvider.Select(p => new ReconciliationProviderOnlyDto
        {
            ProviderMessageSid = p.Sid,
            Status = p.Status,
            ErrorCode = p.ErrorCode,
            From = p.From,
            DateSent = p.DateSent
        }).ToList(),
        OnlyInEShop = report.OnlyInEShop.Select(e => new ReconciliationEShopOnlyDto
        {
            NotificationId = e.NotificationId,
            ProviderMessageSid = e.ProviderMessageSid,
            Kind = e.Kind.ToString(),
            OrderId = e.OrderId,
            LocalStatus = e.LocalStatus,
            CreatedAt = e.CreatedAt
        }).ToList()
    };
}

public class ReconciliationMatchDto
{
    public int NotificationId { get; set; }
    public string ProviderMessageSid { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public string? LocalStatus { get; set; }
    public string? ProviderStatus { get; set; }
    public bool StatusMatches { get; set; }
}

public class ReconciliationProviderOnlyDto
{
    public string ProviderMessageSid { get; set; } = string.Empty;
    public string? Status { get; set; }
    public int? ErrorCode { get; set; }
    public string? From { get; set; }
    public DateTimeOffset? DateSent { get; set; }
}

public class ReconciliationEShopOnlyDto
{
    public int NotificationId { get; set; }
    public string? ProviderMessageSid { get; set; }
    public string Kind { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public string LocalStatus { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

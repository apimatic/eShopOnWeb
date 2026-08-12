using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconciliationMatchDto
{
    public string Sid { get; set; } = string.Empty;
    public string? ProviderStatus { get; set; }
    public int NotificationId { get; set; }
    public string? LocalStatus { get; set; }
}

public class ProviderMessageDto
{
    public string Sid { get; set; } = string.Empty;
    public string? Status { get; set; }
    public string? From { get; set; }
    /// <summary>Destination masked to the last four digits, so numbers are not exposed in full.</summary>
    public string? ToMasked { get; set; }
    public DateTimeOffset? DateSent { get; set; }
    public int? ErrorCode { get; set; }
}

public class EShopOnlyDto
{
    public int NotificationId { get; set; }
    public string? Sid { get; set; }
    public string? Status { get; set; }
    public int OrderId { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;

    public int MatchedCount { get; set; }
    public int OnlyAtProviderCount { get; set; }
    public int OnlyInEShopCount { get; set; }

    /// <summary>Messages the provider and eShop agree on.</summary>
    public List<ReconciliationMatchDto> Matched { get; set; } = new();

    /// <summary>Messages the provider knows about that eShop does not (e.g. the account's other traffic).</summary>
    public List<ProviderMessageDto> OnlyAtProvider { get; set; } = new();

    /// <summary>Messages eShop believes it sent that the provider's range query did not return.</summary>
    public List<EShopOnlyDto> OnlyInEShop { get; set; } = new();
}

/// <summary>
/// GET /api/notifications/reconciliation?from={from}&amp;to={to} — lists the provider's own record of
/// messages from the configured sending number over a date range and lines them up against what eShop
/// believes it sent. Restricted to administrators.
/// </summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to, INotificationService service) =>
            {
                if (!TryParseIso(from, out var fromDate) || !TryParseIso(to, out var toDate))
                {
                    return Results.BadRequest(new { error = "'from' and 'to' are required ISO-8601 date-times." });
                }
                if (fromDate > toDate)
                {
                    return Results.BadRequest(new { error = "'from' must not be after 'to'." });
                }

                var report = await service.ReconcileAsync(fromDate, toDate);

                var response = new ReconciliationResponse
                {
                    From = report.From,
                    To = report.To,
                    FromNumber = report.FromNumber,
                    MatchedCount = report.Matched.Count,
                    OnlyAtProviderCount = report.OnlyAtProvider.Count,
                    OnlyInEShopCount = report.OnlyInEShop.Count,
                    Matched = report.Matched.Select(m => new ReconciliationMatchDto
                    {
                        Sid = m.Sid,
                        ProviderStatus = m.ProviderStatus,
                        NotificationId = m.NotificationId,
                        LocalStatus = m.LocalStatus
                    }).ToList(),
                    OnlyAtProvider = report.OnlyAtProvider.Select(p => new ProviderMessageDto
                    {
                        Sid = p.Sid,
                        Status = p.Status,
                        From = p.From,
                        ToMasked = Mask(p.To),
                        DateSent = p.DateSent,
                        ErrorCode = p.ErrorCode
                    }).ToList(),
                    OnlyInEShop = report.OnlyInEShop.Select(n => new EShopOnlyDto
                    {
                        NotificationId = n.Id,
                        Sid = n.ProviderMessageSid,
                        Status = n.Status,
                        OrderId = n.OrderId
                    }).ToList()
                };
                return Results.Ok(response);
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    private static bool TryParseIso(string? value, out DateTimeOffset result)
    {
        result = default;
        return !string.IsNullOrWhiteSpace(value)
            && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces, out result);
    }

    private static string? Mask(string? number)
    {
        if (string.IsNullOrEmpty(number))
        {
            return null;
        }
        return number.Length <= 4 ? "****" : "****" + number[^4..];
    }
}

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
/// Operator action: lists the provider's own record of messages for a date range (from the app's
/// configured sending number only) and lines them up against what eShop believes it sent — so a
/// message the provider knows about and eShop doesn't, or the reverse, is visible.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, ISmsNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to, ISmsNotificationService service) =>
            {
                if (!TryParseIso(from, out var fromDt) || !TryParseIso(to, out var toDt))
                    return Results.BadRequest(new { message = "'from' and 'to' must be ISO-8601 date-times." });
                if (toDt < fromDt)
                    return Results.BadRequest(new { message = "'to' must not be earlier than 'from'." });

                return await HandleAsync(new ReconciliationRequest { From = fromDt, To = toDt }, service);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, ISmsNotificationService service)
    {
        var report = await service.ReconcileAsync(request.From, request.To);
        var response = new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            FromNumber = report.FromNumber,
            InBothCount = report.InBothCount,
            ProviderOnlyCount = report.ProviderOnlyCount,
            EshopOnlyCount = report.EshopOnlyCount,
            Truncated = report.Truncated,
            Entries = report.Entries.Select(e => new ReconciliationEntryDto
            {
                Sid = e.Sid,
                KnownToProvider = e.KnownToProvider,
                KnownToEshop = e.KnownToEshop,
                ProviderStatus = e.ProviderStatus,
                ProviderDateSent = e.ProviderDateSent,
                EshopNotificationId = e.EshopNotificationId,
                EshopOutcome = e.EshopOutcome
            }).ToList()
        };
        return Results.Ok(response);
    }

    private static bool TryParseIso(string? value, out DateTimeOffset result) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result);
}

public class ReconciliationRequest : BaseRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class ReconciliationEntryDto
{
    public string? Sid { get; set; }
    public bool KnownToProvider { get; set; }
    public bool KnownToEshop { get; set; }
    public string? ProviderStatus { get; set; }
    public DateTimeOffset? ProviderDateSent { get; set; }
    public int? EshopNotificationId { get; set; }
    public string? EshopOutcome { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public int InBothCount { get; set; }
    public int ProviderOnlyCount { get; set; }
    public int EshopOnlyCount { get; set; }
    public bool Truncated { get; set; }
    public List<ReconciliationEntryDto> Entries { get; set; } = new();
}

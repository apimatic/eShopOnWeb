using System;
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
/// Operator report: lists the provider's own record of messages for a date range (for this app's
/// configured sending number only) and lines them up against what eShop believes it sent, so a message
/// one side has and the other doesn't is visible. from/to are ISO-8601 date-times.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, string?, string?>
{
    private readonly INotificationService _notificationService;

    public ReconciliationEndpoint(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to) =>
            {
                return await HandleAsync(from, to);
            })
            .Produces<ReconciliationResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(string? from, string? to)
    {
        if (!TryParse(from, out var fromUtc) || !TryParse(to, out var toUtc))
            return Results.BadRequest(new { message = "'from' and 'to' must be ISO-8601 date-times." });

        if (fromUtc > toUtc)
            return Results.BadRequest(new { message = "'from' must not be after 'to'." });

        var report = await _notificationService.ReconcileAsync(fromUtc, toUtc);

        var response = new ReconciliationResponse
        {
            FromUtc = report.FromUtc,
            ToUtc = report.ToUtc,
            FromNumber = report.FromNumber,
            ProviderCount = report.Matched.Count + report.OnlyAtProvider.Count,
            EShopCount = report.Matched.Count + report.OnlyInEShop.Count,
            MatchedCount = report.Matched.Count,
            Matched = report.Matched.Select(m => new ReconciliationMatchDto
            {
                Sid = m.Sid,
                NotificationId = m.NotificationId,
                ProviderStatus = m.ProviderStatus,
                EShopStatus = m.EShopStatus,
                StatusMatches = m.StatusMatches
            }).ToList(),
            OnlyAtProvider = report.OnlyAtProvider.Select(p => new ProviderOnlyDto
            {
                Sid = p.Sid,
                Status = p.Status,
                To = PhoneNumberMasking.Mask(p.To),
                DateSent = p.DateSent,
                ErrorCode = p.ErrorCode
            }).ToList(),
            OnlyInEShop = report.OnlyInEShop.Select(n => new EShopOnlyDto
            {
                NotificationId = n.Id,
                OrderId = n.OrderId,
                Sid = n.ProviderSid,
                Status = n.Status
            }).ToList()
        };
        return Results.Ok(response);
    }

    private static bool TryParse(string? value, out DateTimeOffset result)
    {
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result);
    }
}

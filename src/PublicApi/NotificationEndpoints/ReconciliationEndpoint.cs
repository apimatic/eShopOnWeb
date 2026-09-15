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
/// Operator report: the provider's own record of messages sent from this application's configured
/// sending number over a date range, lined up against what eShop believes it sent, so a message the
/// provider knows about and eShop doesn't — or the reverse — is visible.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to, IOrderNotificationService notificationService) =>
                await HandleAsync(new ReconciliationRequest(from, to), notificationService))
            .Produces<ReconciliationResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IOrderNotificationService notificationService)
    {
        if (!TryParseIso(request.From, out var from) || !TryParseIso(request.To, out var to))
        {
            return Results.Problem("'from' and 'to' must be ISO-8601 date-times.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (from > to)
        {
            return Results.Problem("'from' must not be after 'to'.", statusCode: StatusCodes.Status400BadRequest);
        }

        var report = await notificationService.ReconcileAsync(from, to);

        var response = new ReconciliationResponse(request.CorrelationId())
        {
            From = report.From,
            To = report.To,
            MatchedCount = report.Matched.Count,
            ProviderOnlyCount = report.ProviderOnly.Count,
            EShopOnlyCount = report.EShopOnly.Count,
            Matched = report.Matched.Select(m => new ReconciliationMatchDto
            {
                ProviderMessageId = m.ProviderMessageId,
                ProviderStatus = m.ProviderStatus,
                NotificationId = m.NotificationId,
                OrderId = m.OrderId,
                EShopStatus = m.EShopStatus
            }).ToList(),
            ProviderOnly = report.ProviderOnly.Select(p => new ProviderOnlyMessageDto
            {
                ProviderMessageId = p.ProviderMessageId,
                ProviderStatus = p.ProviderStatus,
                DateSent = p.DateSent
            }).ToList(),
            EShopOnly = report.EShopOnly.Select(e => new EShopOnlyNotificationDto
            {
                NotificationId = e.NotificationId,
                OrderId = e.OrderId,
                ProviderMessageId = e.ProviderMessageId,
                EShopStatus = e.EShopStatus
            }).ToList()
        };

        return Results.Ok(response);
    }

    private static bool TryParseIso(string? value, out DateTimeOffset result)
    {
        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind | DateTimeStyles.AssumeUniversal,
            out result) && !string.IsNullOrWhiteSpace(value);
    }
}

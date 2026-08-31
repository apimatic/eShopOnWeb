using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Messaging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Reconciliation report (operator): the provider's own record of messages sent from this
/// application's configured sending number over the range, lined up against what eShop
/// believes it sent. Messages the provider knows about and eShop doesn't — and the reverse —
/// are listed separately.
/// </summary>
public class ReconciliationEndpoint : IEndpoint
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
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(DateTimeOffset from, DateTimeOffset to)
    {
        if (from >= to)
        {
            return Results.BadRequest("'from' must be earlier than 'to'. Both are ISO-8601 date-times.");
        }

        var report = await _notificationService.ReconcileAsync(from, to);

        var response = new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            Truncated = report.Truncated,
            Matched = report.Matched.Select(ToDto).ToList(),
            ProviderOnly = report.ProviderOnly.Select(ToDto).ToList(),
            LocalOnly = report.LocalOnly.Select(ToDto).ToList()
        };
        response.MatchedCount = response.Matched.Count;
        response.ProviderOnlyCount = response.ProviderOnly.Count;
        response.LocalOnlyCount = response.LocalOnly.Count;
        return Results.Ok(response);
    }

    private static ReconciliationEntryDto ToDto(ReconciliationEntry entry) => new()
    {
        ProviderMessageId = entry.ProviderMessageId,
        NotificationId = entry.NotificationId,
        OrderId = entry.OrderId,
        ProviderStatus = entry.ProviderStatus,
        LocalStatus = entry.LocalStatus,
        DateSent = entry.DateSent,
        ProviderBody = entry.ProviderBody
    };
}

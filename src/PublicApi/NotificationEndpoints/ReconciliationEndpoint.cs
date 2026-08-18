using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator report: lists the provider's own record of messages from this application's configured sending
/// number over a date range and lines them up against what eShop believes it sent, so a message one side
/// knows about and the other does not is visible. <c>from</c> and <c>to</c> are ISO-8601 date-times.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest>
{
    private readonly IOrderNotificationService _service;

    public ReconciliationEndpoint(IOrderNotificationService service)
    {
        _service = service;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string from, string to) =>
            {
                return await HandleAsync(new ReconciliationRequest(from, to));
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request)
    {
        if (!TryParse(request.From, out var from) || !TryParse(request.To, out var to))
        {
            return Results.BadRequest("'from' and 'to' must be ISO-8601 date-times.");
        }
        if (to < from)
        {
            return Results.BadRequest("'to' must not be earlier than 'from'.");
        }

        try
        {
            var report = await _service.ReconcileAsync(from, to);
            var response = new ReconciliationResponse(request.CorrelationId())
            {
                From = report.From,
                To = report.To,
                FromNumber = report.FromNumber,
                MatchedCount = report.MatchedCount,
                ProviderOnlyCount = report.ProviderOnlyCount,
                EShopOnlyCount = report.EShopOnlyCount,
                Matched = report.Matched.Select(ToDto).ToList(),
                ProviderOnly = report.ProviderOnly.Select(ToDto).ToList(),
                EShopOnly = report.EShopOnly.Select(ToDto).ToList()
            };
            return Results.Ok(response);
        }
        catch (SmsGatewayException ex)
        {
            return ProviderErrorResults.From(ex);
        }
    }

    private static bool TryParse(string value, out DateTimeOffset result) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out result);

    private static ReconciliationEntryDto ToDto(ReconciliationEntry e) => new()
    {
        Sid = e.Sid,
        ProviderStatus = e.ProviderStatus,
        EShopStatus = e.EShopStatus,
        OrderId = e.OrderId,
        DateSent = e.DateSent
    };
}

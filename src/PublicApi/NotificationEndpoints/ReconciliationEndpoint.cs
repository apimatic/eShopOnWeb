using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator report: lists the provider's own record of messages sent from this application's
/// configured sending number over a date range and lines them up against what eShop believes
/// it sent, so a discrepancy either way is visible. <c>from</c>/<c>to</c> are ISO-8601 date-times.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, INotificationOperationsService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            ([FromQuery] DateTimeOffset from, [FromQuery] DateTimeOffset to, INotificationOperationsService service) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to }, service);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, INotificationOperationsService service)
    {
        if (request.To < request.From)
            return Results.BadRequest("'to' must not be earlier than 'from'.");

        var report = await service.ReconcileAsync(request.From, request.To);

        var response = new ReconciliationResponse(request.CorrelationId())
        {
            From = report.From,
            To = report.To,
            FromNumber = report.FromNumber,
            ProviderMessageCount = report.ProviderMessageCount,
            EShopMessageCount = report.EShopMessageCount,
            MatchedCount = report.MatchedCount,
            OnlyAtProvider = report.OnlyAtProvider.Select(e => new ReconciliationEntryDto(e.MessageSid, e.Status, e.NotificationId)).ToList(),
            OnlyInEShop = report.OnlyInEShop.Select(e => new ReconciliationEntryDto(e.MessageSid, e.Status, e.NotificationId)).ToList(),
            Matched = report.Matched.Select(m => new ReconciliationMatchDto(m.MessageSid, m.ProviderStatus, m.EShopStatus, m.NotificationId)).ToList()
        };
        return Results.Ok(response);
    }
}

public class ReconciliationRequest : BaseRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }
    public ReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public int ProviderMessageCount { get; set; }
    public int EShopMessageCount { get; set; }
    public int MatchedCount { get; set; }
    public List<ReconciliationEntryDto> OnlyAtProvider { get; set; } = new();
    public List<ReconciliationEntryDto> OnlyInEShop { get; set; } = new();
    public List<ReconciliationMatchDto> Matched { get; set; } = new();
}

public record ReconciliationEntryDto(string? MessageSid, string? Status, int? NotificationId);

public record ReconciliationMatchDto(string MessageSid, string ProviderStatus, string EShopStatus, int NotificationId);

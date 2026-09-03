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

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IShopperOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string from, string to, IShopperOrderService service, HttpContext http) =>
            {
                return await HandleAsync(new ReconciliationRequest(from, to), service, http.RequestAborted);
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(ReconciliationRequest request, IShopperOrderService service) =>
        HandleAsync(request, service, default);

    private async Task<IResult> HandleAsync(
        ReconciliationRequest request,
        IShopperOrderService service,
        System.Threading.CancellationToken cancellationToken)
    {
        if (!TryParse(request.From, out var from) || !TryParse(request.To, out var to))
        {
            return Results.BadRequest("from and to must be ISO-8601 date-times.");
        }

        var report = await service.ReconcileAsync(from, to, cancellationToken);
        return Results.Ok(new ReconciliationResponse(request.CorrelationId())
        {
            From = report.From,
            To = report.To,
            ProviderCount = report.ProviderCount,
            LocalCount = report.LocalCount,
            MatchedCount = report.MatchedCount,
            ProviderOnlyCount = report.ProviderOnlyCount,
            LocalOnlyCount = report.LocalOnlyCount,
            Matches = report.Matches.Select(m => new ReconciliationMatchDto
            {
                ProviderSid = m.ProviderSid,
                NotificationId = m.NotificationId,
                ProviderStatus = m.ProviderStatus,
                LocalStatus = m.LocalStatus,
                Alignment = m.Alignment
            }).ToList()
        });
    }

    private static bool TryParse(string value, out DateTimeOffset parsed) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsed);
}

public class ReconciliationRequest : BaseRequest
{
    public string From { get; init; }
    public string To { get; init; }

    public ReconciliationRequest(string from, string to)
    {
        From = from;
        To = to;
    }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }
    public ReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int ProviderCount { get; set; }
    public int LocalCount { get; set; }
    public int MatchedCount { get; set; }
    public int ProviderOnlyCount { get; set; }
    public int LocalOnlyCount { get; set; }
    public System.Collections.Generic.List<ReconciliationMatchDto> Matches { get; set; } = new();
}

public class ReconciliationMatchDto
{
    public string? ProviderSid { get; set; }
    public int? NotificationId { get; set; }
    public string? ProviderStatus { get; set; }
    public string? LocalStatus { get; set; }
    public string Alignment { get; set; } = string.Empty;
}

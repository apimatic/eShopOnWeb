using System;
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

public class NotificationReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, INotificationOperatorService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (string from, string to, INotificationOperatorService service) =>
            {
                return await HandleAsync(new ReconciliationRequest(from, to), service);
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, INotificationOperatorService service)
    {
        if (!DateTimeOffset.TryParse(request.From, out var from)
            || !DateTimeOffset.TryParse(request.To, out var to))
        {
            return Results.BadRequest(new { message = "from and to must be ISO-8601 date-times." });
        }

        var report = await service.ReconcileAsync(from, to);
        var response = new ReconciliationResponse(request.CorrelationId())
        {
            From = report.From,
            To = report.To,
            FromNumber = report.FromNumber,
            MatchedCount = report.MatchedCount,
            ProviderOnlyCount = report.ProviderOnlyCount,
            EshopOnlyCount = report.EshopOnlyCount,
            Entries = report.Entries.Select(e => new ReconciliationEntryDto
            {
                NotificationId = e.NotificationId,
                ProviderMessageSid = e.ProviderMessageSid,
                Kind = e.Kind,
                EshopStatus = e.EshopStatus,
                ProviderStatus = e.ProviderStatus,
                Match = e.Match
            }).ToList()
        };
        return Results.Ok(response);
    }
}

public class ReconciliationRequest : BaseRequest
{
    public ReconciliationRequest(string from, string to)
    {
        From = from;
        To = to;
    }

    public string From { get; }
    public string To { get; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId)
    {
    }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public int MatchedCount { get; set; }
    public int ProviderOnlyCount { get; set; }
    public int EshopOnlyCount { get; set; }
    public System.Collections.Generic.List<ReconciliationEntryDto> Entries { get; set; } = new();
}

public class ReconciliationEntryDto
{
    public string? NotificationId { get; set; }
    public string? ProviderMessageSid { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? EshopStatus { get; set; }
    public string? ProviderStatus { get; set; }
    public string Match { get; set; } = string.Empty;
}

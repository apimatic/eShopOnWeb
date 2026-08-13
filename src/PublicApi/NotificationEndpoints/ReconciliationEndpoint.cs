using System;
using System.Collections.Generic;
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

public class ReconciliationRequest : BaseRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public int ProviderCount { get; set; }
    public int EShopCount { get; set; }
    public int MatchedCount { get; set; }
    public int ProviderOnlyCount { get; set; }
    public int EShopOnlyCount { get; set; }
    public IReadOnlyList<ReconciliationEntry> Matched { get; set; } = new List<ReconciliationEntry>();
    public IReadOnlyList<ReconciliationEntry> ProviderOnly { get; set; } = new List<ReconciliationEntry>();
    public IReadOnlyList<ReconciliationEntry> EShopOnly { get; set; } = new List<ReconciliationEntry>();
}

/// <summary>
/// Operator action: reconciles the provider's own record of messages for a date range against what eShop
/// believes it sent, so a message one side knows about and the other does not becomes visible. Counts only
/// messages sent from this application's configured sending number. Restricted to administrators.
/// <c>from</c> and <c>to</c> are ISO-8601 date-times.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, ISmsNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            ([FromQuery(Name = "from")] DateTimeOffset from, [FromQuery(Name = "to")] DateTimeOffset to, ISmsNotificationService service) =>
                await HandleAsync(new ReconciliationRequest { From = from, To = to }, service))
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, ISmsNotificationService service)
    {
        if (request.To < request.From)
            return Results.BadRequest(new { message = "'to' must be on or after 'from'." });

        var report = await service.ReconcileAsync(request.From, request.To);

        var response = new ReconciliationResponse(request.CorrelationId())
        {
            From = report.From,
            To = report.To,
            FromNumber = report.FromNumber,
            ProviderCount = report.ProviderCount,
            EShopCount = report.EShopCount,
            MatchedCount = report.Matched.Count,
            ProviderOnlyCount = report.ProviderOnly.Count,
            EShopOnlyCount = report.EShopOnly.Count,
            Matched = report.Matched,
            ProviderOnly = report.ProviderOnly,
            EShopOnly = report.EShopOnly
        };
        return Results.Ok(response);
    }
}

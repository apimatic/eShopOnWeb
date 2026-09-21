using System;
using System.Collections.Generic;
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

public class ReconciliationEntryDto
{
    public string Sid { get; set; } = string.Empty;
    public string? Status { get; set; }
    public string? To { get; set; }
    public string Source { get; set; } = string.Empty;
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int ProviderCount { get; set; }
    public int EShopCount { get; set; }
    public int MatchedCount { get; set; }
    public List<ReconciliationEntryDto> Entries { get; set; } = new();
}

/// <summary>
/// Operator action: reconcile the provider's own record of messages sent from this application's
/// configured sending number against what eShop believes it sent, over a date range. Admin only.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationEndpoint.ReconciliationQuery>
{
    public record ReconciliationQuery(DateTimeOffset From, DateTimeOffset To);

    private readonly IOrderMessagingService _orderMessagingService;

    public ReconciliationEndpoint(IOrderMessagingService orderMessagingService)
    {
        _orderMessagingService = orderMessagingService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to) => await HandleAsync(new ReconciliationQuery(from, to)))
            .Produces<ReconciliationResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationQuery query)
    {
        if (query.To < query.From)
            return Results.BadRequest(new { error = "'to' must be on or after 'from'." });

        using var cts = EndpointBudget.Start();
        try
        {
            var report = await _orderMessagingService.ReconcileAsync(query.From, query.To, cts.Token);
            var response = new ReconciliationResponse
            {
                From = report.From,
                To = report.To,
                ProviderCount = report.ProviderCount,
                EShopCount = report.EShopCount,
                MatchedCount = report.MatchedCount,
                Entries = report.Entries.Select(e => new ReconciliationEntryDto
                {
                    Sid = e.Sid,
                    Status = e.Status,
                    To = e.To,
                    Source = e.Source
                }).ToList()
            };
            return Results.Ok(response);
        }
        catch (SmsGatewayException ex)
        {
            return Results.Problem(
                title: "The messaging provider could not be queried for reconciliation.",
                detail: ex.Message,
                statusCode: StatusCodes.Status502BadGateway);
        }
    }
}

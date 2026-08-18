using System;
using System.Collections.Generic;
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
/// Operator report: lists the provider's own record of messages (sent from the configured sending
/// number) over a date range and lines them up against what eShop believes it sent, so an either-way
/// discrepancy is visible. <c>from</c> and <c>to</c> are ISO-8601 date-times.
/// </summary>
public class NotificationReconciliationEndpoint : IEndpoint<IResult, DateTimeOffset, DateTimeOffset, HttpContext>
{
    private readonly IOrderNotificationService _orderNotificationService;

    public NotificationReconciliationEndpoint(IOrderNotificationService orderNotificationService)
    {
        _orderNotificationService = orderNotificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, HttpContext http) => await HandleAsync(from, to, http))
            .Produces<ReconciliationResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(DateTimeOffset from, DateTimeOffset to, HttpContext http)
    {
        if (to < from)
            return Results.BadRequest("'to' must be on or after 'from'.");

        try
        {
            var report = await _orderNotificationService.ReconcileAsync(from, to, http.RequestAborted);

            var response = new ReconciliationResponse
            {
                From = report.From,
                To = report.To,
                ProviderMessageCount = report.ProviderMessageCount,
                EShopMessageCount = report.EShopMessageCount,
                Matched = report.Matched,
                ProviderOnly = report.ProviderOnly,
                EShopOnly = report.EShopOnly
            };
            return Results.Ok(response);
        }
        catch (SmsGatewayException)
        {
            return Results.Problem("The messaging provider could not be reached to build the reconciliation report.",
                statusCode: StatusCodes.Status502BadGateway);
        }
    }
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int ProviderMessageCount { get; set; }
    public int EShopMessageCount { get; set; }
    public IReadOnlyList<ReconciliationEntry> Matched { get; set; } = new List<ReconciliationEntry>();
    public IReadOnlyList<ReconciliationEntry> ProviderOnly { get; set; } = new List<ReconciliationEntry>();
    public IReadOnlyList<ReconciliationEntry> EShopOnly { get; set; } = new List<ReconciliationEntry>();
}

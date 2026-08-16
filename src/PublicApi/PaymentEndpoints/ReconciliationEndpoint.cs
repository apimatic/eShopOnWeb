using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Operator report: lists PayPal's own record of transactions for a date range and lines them up
/// against eShop orders, covering the whole range. <c>from</c>/<c>to</c> are ISO-8601 date-times.
/// Restricted to administrators.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationEndpoint.Range, IPaymentOrderService>
{
    public record struct Range(DateTimeOffset From, DateTimeOffset To);

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            ([FromQuery] DateTimeOffset from, [FromQuery] DateTimeOffset to, IPaymentOrderService service, CancellationToken ct) =>
            {
                return await HandleAsync(new Range(from, to), service, ct);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("OrderPaymentEndpoints");
    }

    public Task<IResult> HandleAsync(Range range, IPaymentOrderService service) =>
        HandleAsync(range, service, CancellationToken.None);

    public async Task<IResult> HandleAsync(Range range, IPaymentOrderService service, CancellationToken ct)
    {
        if (range.To < range.From)
        {
            return Results.BadRequest(new { message = "'to' must be on or after 'from'." });
        }

        var report = await service.ReconcileAsync(range.From, range.To, ct);
        var response = new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            PayPalTransactionCount = report.PayPalTransactionCount,
            MatchedCount = report.MatchedCount,
            PayPalOnlyCount = report.PayPalOnlyCount,
            EShopOnlyCount = report.EShopOnlyCount,
            Entries = report.Entries.ToList()
        };
        return Results.Ok(response);
    }
}

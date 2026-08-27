using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int TotalPayPalTransactions { get; set; }
    public int MatchedTransactions { get; set; }
    public int UnmatchedTransactions { get; set; }
    public List<ReconciliationRow> Transactions { get; set; } = new();
    public List<UnmatchedPayment> PaymentsMissingFromPayPal { get; set; } = new();
}

/// <summary>
/// Operator action: lists PayPal's own record of transactions for a date range and
/// lines them up against eShop orders, so a payment known on only one side is visible.
/// Covers the whole range, not just the first page.
/// </summary>
public class GetReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            ([FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to,
                IOrderPaymentService orderPaymentService, CancellationToken cancellationToken) =>
            {
                if (from == null || to == null)
                {
                    return Results.BadRequest(new { message = "Both 'from' and 'to' query parameters (ISO-8601 date-times) are required." });
                }
                if (to <= from)
                {
                    return Results.BadRequest(new { message = "'to' must be after 'from'." });
                }
                if (to - from > TimeSpan.FromDays(31))
                {
                    // The transaction_search_v1 spec supports a maximum range of 31 days.
                    return Results.BadRequest(new { message = "The range must not exceed 31 days." });
                }

                var report = await orderPaymentService.GetReconciliationAsync(from.Value, to.Value, cancellationToken);
                return Results.Ok(new ReconciliationResponse
                {
                    From = report.From,
                    To = report.To,
                    TotalPayPalTransactions = report.TotalPayPalTransactions,
                    MatchedTransactions = report.MatchedTransactions,
                    UnmatchedTransactions = report.UnmatchedTransactions,
                    Transactions = new List<ReconciliationRow>(report.Transactions),
                    PaymentsMissingFromPayPal = new List<UnmatchedPayment>(report.PaymentsMissingFromPayPal)
                });
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }
}

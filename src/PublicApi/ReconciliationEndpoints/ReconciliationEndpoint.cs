using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

/// <summary>
/// Operator report: lists PayPal's own record of transactions over a date range
/// and lines them up against eShop orders, in both directions.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IOrderPaymentService orderPaymentService) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to }, orderPaymentService);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IOrderPaymentService orderPaymentService)
    {
        var report = await orderPaymentService.GetReconciliationAsync(request.From, request.To);

        var response = new ReconciliationResponse(request.CorrelationId())
        {
            From = report.From,
            To = report.To,
            PayPalTransactionCount = report.Transactions.Count,
            Transactions = report.Transactions,
            PaymentsMissingFromPayPal = report.PaymentsMissingFromPayPal
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
    public int PayPalTransactionCount { get; set; }

    /// <summary>PayPal's record of transactions; MatchedToOrder is false when PayPal
    /// knows about a payment eShop doesn't.</summary>
    public System.Collections.Generic.List<ReconciledTransaction> Transactions { get; set; } = new();

    /// <summary>eShop payments in the range that PayPal's report does not list
    /// (reporting lag or a genuine discrepancy).</summary>
    public System.Collections.Generic.List<UnmatchedPayment> PaymentsMissingFromPayPal { get; set; } = new();
}

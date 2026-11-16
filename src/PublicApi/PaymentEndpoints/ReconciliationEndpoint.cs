using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Operator action: lists Finix transfers for a range and matches them to eShop orders.
/// </summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset? from, DateTimeOffset? to, FinixPaymentService payments, CancellationToken ct) =>
            {
                return await HandleAsync(from, to, payments, ct);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(
        DateTimeOffset? from,
        DateTimeOffset? to,
        FinixPaymentService payments,
        CancellationToken ct = default)
    {
        if (from is null || to is null)
            throw new FinixPaymentException(400, "DATE_RANGE_REQUIRED",
                "Both 'from' and 'to' ISO-8601 date-times are required.");

        var response = new ReconciliationResponse(Guid.NewGuid());
        var report = await payments.ReconcileAsync(from.Value, to.Value, ct);

        response.From = report.From;
        response.To = report.To;
        response.PagesFetched = report.PagesFetched;
        response.Truncated = report.Truncated;
        response.Transactions = report.Transactions;
        response.UnmatchedFinix = report.Transactions.Where(t => !t.Matched).ToList();
        response.UnmatchedEshop = report.UnmatchedEshop;

        return Results.Ok(response);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class GetReconciliationEndpoint : IEndpoint<IResult, ReconciliationQuery, IReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string from, string to, IReconciliationService reconciliation) =>
            {
                return await HandleAsync(new ReconciliationQuery(from, to), reconciliation);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationQuery query, IReconciliationService reconciliation)
    {
        if (!DateTimeOffset.TryParse(query.From, out var from))
        {
            throw new CheckoutException(400, "`from` must be an ISO-8601 date-time.");
        }

        if (!DateTimeOffset.TryParse(query.To, out var to))
        {
            throw new CheckoutException(400, "`to` must be an ISO-8601 date-time.");
        }

        var report = await reconciliation.ReconcileAsync(from, to, CancellationToken.None);
        return Results.Ok(new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            PayPalTransactions = report.PayPalTransactions.ToList(),
            EshopOrdersWithoutPayPalMatch = report.EshopOrdersWithoutPayPalMatch.ToList()
        });
    }
}

public class ReconciliationQuery
{
    public ReconciliationQuery(string from, string to)
    {
        From = from;
        To = to;
    }

    public string From { get; }
    public string To { get; }
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationRow> PayPalTransactions { get; set; } = new();
    public List<UnmatchedOrderRow> EshopOrdersWithoutPayPalMatch { get; set; } = new();
}

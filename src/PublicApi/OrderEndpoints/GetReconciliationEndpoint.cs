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

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class GetReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IPaymentReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IPaymentReconciliationService reconciliation) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to }, reconciliation);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IPaymentReconciliationService reconciliation)
    {
        var report = await reconciliation.ReconcileAsync(request.From, request.To);
        return Results.Ok(new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            PaypalTransactionCount = report.PaypalTransactionCount,
            EshopOrderCount = report.EshopOrderCount,
            Rows = report.Rows.Select(r => new ReconciliationRowDto
            {
                Source = r.Source,
                PaypalTransactionId = r.PaypalTransactionId,
                OrderId = r.OrderId,
                Match = r.Match,
                PaypalStatus = r.PaypalStatus,
                OrderStatus = r.OrderStatus,
                PaypalAmount = r.PaypalAmount,
                OrderAmount = r.OrderAmount,
                Currency = r.Currency,
                PaypalDate = r.PaypalDate
            }).ToList()
        });
    }
}

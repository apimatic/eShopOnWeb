using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: compare Finix's transaction record with eShop orders for a date range.
/// All pages of Finix results are read so the report covers the whole range.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, IOrderPaymentService, DateTimeOffset, DateTimeOffset>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            ([FromQuery] DateTimeOffset from, [FromQuery] DateTimeOffset to, IOrderPaymentService paymentService) =>
            {
                return await HandleAsync(paymentService, from, to);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(IOrderPaymentService paymentService, DateTimeOffset from, DateTimeOffset to)
    {
        var result = await paymentService.ReconcileAsync(from, to);

        var response = new ReconciliationResponse
        {
            From = result.From,
            To = result.To,
            FinixTransactions = result.FinixTransactions,
            Orders = result.Orders,
            UnmatchedFinixTransactions = result.UnmatchedFinixTransactions,
            UnmatchedOrders = result.UnmatchedOrders
        };

        return Results.Ok(response);
    }
}

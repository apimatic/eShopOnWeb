using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: a report of the payment provider's own record of transactions for a
/// date range, lined up against eShop orders.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IOrderPaymentService paymentService) =>
            {
                return await HandleAsync(new ReconciliationRequest(from, to), paymentService);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IOrderPaymentService paymentService)
    {
        var response = new ReconciliationResponse(request.CorrelationId());

        if (request.From > request.To)
        {
            throw new InvalidOrderStateException("'from' must not be after 'to'.", 400);
        }

        var rows = await paymentService.GetReconciliationAsync(request.From, request.To, System.Threading.CancellationToken.None);

        response.From = request.From;
        response.To = request.To;

        foreach (var row in rows)
        {
            response.Rows.Add(new ReconciliationRowDto
            {
                PayPalTransactionId = row.PayPalTransactionId,
                PayPalReferenceId = row.PayPalReferenceId,
                EventCode = row.EventCode,
                Status = row.Status,
                Date = row.Date,
                GrossAmount = row.GrossAmount,
                FeeAmount = row.FeeAmount,
                NetAmount = row.NetAmount,
                Currency = row.Currency,
                PayerEmail = row.PayerEmail,
                OrderId = row.OrderId,
                OrderStatus = row.OrderStatus,
                OrderTotal = row.OrderTotal,
                Relation = row.Relation
            });
        }

        return Results.Ok(response);
    }
}
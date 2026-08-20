using System;
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

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationQuery, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (string from, string to, IOrderPaymentService payments) =>
                await HandleAsync(new ReconciliationQuery(from, to), payments))
            .Produces<ReconciliationResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationQuery request, IOrderPaymentService payments)
    {
        if (!DateTimeOffset.TryParse(request.From, out var from) || !DateTimeOffset.TryParse(request.To, out var to))
        {
            throw new PaymentException("`from` and `to` must be ISO-8601 date-times.", 400);
        }

        var report = await payments.ReconcileAsync(from, to);
        return Results.Ok(new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            PaypalTransactions = report.PaypalTransactions.Select(t => new PayPalTransactionResponse
            {
                TransactionId = t.TransactionId,
                PaypalReferenceId = t.PaypalReferenceId,
                EventCode = t.EventCode,
                Status = t.Status,
                Amount = t.Amount,
                Currency = t.Currency,
                InvoiceId = t.InvoiceId,
                CustomField = t.CustomField,
                InitiationTime = t.InitiationTime
            }).ToList(),
            Orders = report.LocalOrders.Select(PaymentApiMapper.FromOrder).ToList(),
            Mismatches = report.Mismatches.Select(m => new ReconciliationMismatchResponse
            {
                Kind = m.Kind,
                Identifier = m.Identifier,
                Detail = m.Detail
            }).ToList()
        });
    }
}

public record ReconciliationQuery(string From, string To);

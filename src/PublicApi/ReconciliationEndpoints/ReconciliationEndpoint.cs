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

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationQuery, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (DateTimeOffset from, DateTimeOffset to, IOrderPaymentService service) =>
            {
                return await HandleAsync(new ReconciliationQuery(from, to), service);
            })
            .Produces<ReconciliationApiResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationQuery request, IOrderPaymentService service)
    {
        var report = await service.ReconcileAsync(request.From, request.To);
        return Results.Ok(new ReconciliationApiResponse
        {
            From = report.From,
            To = report.To,
            Transactions = report.Matches.Select(m => new ReconciliationTransactionDto
            {
                TransactionId = m.PayPalTransaction.TransactionId,
                InvoiceId = m.PayPalTransaction.InvoiceId,
                CustomField = m.PayPalTransaction.CustomField,
                ReferenceId = m.PayPalTransaction.ReferenceId,
                EventCode = m.PayPalTransaction.EventCode,
                Status = m.PayPalTransaction.Status,
                Amount = m.PayPalTransaction.Amount,
                FeeAmount = m.PayPalTransaction.FeeAmount,
                Currency = m.PayPalTransaction.Currency,
                InitiationDate = m.PayPalTransaction.InitiationDate,
                OrderId = m.OrderId,
                MatchStatus = m.MatchStatus
            }).ToList(),
            EshopOrdersMissingFromPayPal = report.EshopOrdersMissingFromPayPal.ToList()
        });
    }
}

public record ReconciliationQuery(DateTimeOffset From, DateTimeOffset To);

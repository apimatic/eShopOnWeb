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

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>
/// Operator reconciliation: lists the provider's own record of bills raised in a date range and lines
/// them up against what eShop believes it raised, so a bill the provider knows about and eShop doesn't
/// — or the reverse — is visible. The provider account carries bills that are not this application's,
/// and each entry is classified so it is plain which is which.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IInvoiceService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/invoices/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset? from, DateTimeOffset? to, IInvoiceService invoiceService) =>
            {
                if (from is null || to is null)
                {
                    return Results.BadRequest(new ErrorResponse("Both 'from' and 'to' ISO-8601 date-times are required."));
                }

                return await HandleAsync(new ReconciliationRequest(from.Value, to.Value), invoiceService);
            })
            .Produces<ReconciliationResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("InvoiceEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IInvoiceService invoiceService)
    {
        var result = await invoiceService.ReconcileAsync(request.From, request.To);
        return InvoiceApiResults.ToHttp(result, report => Results.Ok(new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            Summary = new ReconciliationSummaryResponse
            {
                TotalProviderInvoicesInRange = report.Summary.TotalProviderInvoicesInRange,
                Matched = report.Summary.Matched,
                ProviderOnly = report.Summary.ProviderOnly,
                EShopOnly = report.Summary.EShopOnly
            },
            Entries = report.Entries.Select(e => new ReconciliationEntryResponse
            {
                InvoiceId = e.InvoiceId,
                Classification = e.Classification,
                BearsEShopMarker = e.BearsEShopMarker,
                ProviderStatus = e.ProviderStatus,
                Amount = e.Amount,
                Currency = e.Currency,
                CustomerName = e.CustomerName,
                RaisedAt = e.RaisedAt,
                OrderId = e.OrderId,
                BuyerId = e.BuyerId
            }).ToList()
        }));
    }
}

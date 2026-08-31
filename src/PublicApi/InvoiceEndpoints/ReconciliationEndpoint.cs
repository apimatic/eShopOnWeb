using System;
using System.Linq;
using System.Threading;
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
/// Operator report: the provider's own record of bills raised in a date range, lined up against what
/// eShop believes it raised. Makes plain which bills are eShop's and which belong to the shared
/// account's other activity. Restricted to the administrator role.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IInvoiceService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/invoices/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                DateTimeOffset from,
                DateTimeOffset to,
                IInvoiceService invoiceService,
                CancellationToken ct) =>
            {
                return await ExecuteAsync(new ReconciliationRequest { From = from, To = to }, invoiceService, ct);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("InvoiceEndpoints");
    }

    public Task<IResult> HandleAsync(ReconciliationRequest request, IInvoiceService invoiceService) =>
        ExecuteAsync(request, invoiceService, CancellationToken.None);

    private static async Task<IResult> ExecuteAsync(ReconciliationRequest request,
        IInvoiceService invoiceService, CancellationToken ct)
    {
        if (request.From > request.To)
        {
            return Results.BadRequest("'from' must not be after 'to'.");
        }

        var report = await invoiceService.ReconcileAsync(request.From, request.To, ct);

        var response = new ReconciliationResponse(request.CorrelationId())
        {
            From = report.From,
            To = report.To,
            ProviderInvoiceCount = report.ProviderInvoiceCount,
            EShopInvoiceCount = report.EShopInvoiceCount,
            MatchedCount = report.MatchedCount,
            ProviderOnlyCount = report.ProviderOnlyCount,
            EShopOnlyCount = report.EShopOnlyCount,
            Entries = report.Entries.Select(e => new ReconciliationEntryDto
            {
                InvoiceId = e.InvoiceId,
                Presence = e.Presence.ToString(),
                IsEShopInvoice = e.IsEShopInvoice,
                ProviderStatus = e.ProviderStatus,
                ProviderCreatedDate = e.ProviderCreatedDate,
                OrderId = e.OrderId,
                LocalStatus = e.LocalStatus,
                Amount = e.Amount,
                Currency = e.Currency
            }).ToList()
        };

        return Results.Ok(response);
    }
}

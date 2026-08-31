using System;
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
/// Lists the provider's own record of bills raised in a date range, lined up against what
/// eShop believes it raised, so drift in either direction is visible. Because the provider
/// account also carries bills that are not this application's, each entry is marked as
/// belonging to eShop or not. Operator action.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IInvoiceService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/invoices/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IInvoiceService invoiceService) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to }, invoiceService);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("InvoiceEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IInvoiceService invoiceService)
    {
        if (request.From > request.To)
        {
            return Results.BadRequest(new { errors = new[] { "'from' must be earlier than or equal to 'to'." } });
        }

        var report = await invoiceService.ReconcileAsync(request.From, request.To);

        return Results.Ok(new ReconciliationResponse(request.CorrelationId())
        {
            Report = InvoiceDtoMapper.ToDto(report)
        });
    }
}

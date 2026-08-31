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
/// Operator report: lists the provider's own record of bills raised in a date range and lines them up
/// against what eShop believes it raised — so a bill the provider knows about and eShop does not (or
/// the reverse) is visible. The provider account carries bills that are not this application's; the
/// report makes plain which is which. Restricted to the administrator role.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationQuery, IInvoiceService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/invoices/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                       AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset? from, DateTimeOffset? to, IInvoiceService invoiceService) =>
            {
                if (from is null || to is null)
                {
                    return Results.BadRequest("Both 'from' and 'to' ISO-8601 date-times are required.");
                }
                if (from > to)
                {
                    return Results.BadRequest("'from' must not be after 'to'.");
                }
                return await HandleAsync(new ReconciliationQuery { From = from.Value, To = to.Value }, invoiceService);
            })
            .Produces<ReconciliationResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("InvoiceEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationQuery request, IInvoiceService invoiceService)
    {
        var report = await invoiceService.ReconcileAsync(request.From, request.To);
        return Results.Ok(InvoiceDtoMapper.ToResponse(report, request.CorrelationId()));
    }
}

public class ReconciliationQuery : BaseRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

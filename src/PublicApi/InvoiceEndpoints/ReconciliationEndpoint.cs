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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>
/// Operator action: reconciles the provider's own record of bills raised in a date range against what
/// eShop believes it raised, making plain which bills are eShop's, which the provider knows about but
/// eShop does not, and which eShop has no provider record for. Restricted to the administrator role.
/// </summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/invoices/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IInvoicingService service, CancellationToken cancellationToken) =>
                await HandleAsync(from, to, service, cancellationToken))
            .Produces<ReconciliationResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("Invoices");
    }

    public async Task<IResult> HandleAsync(DateTimeOffset from, DateTimeOffset to, IInvoicingService service, CancellationToken cancellationToken)
    {
        var result = await service.ReconcileAsync(from, to, cancellationToken);
        if (!result.IsSuccess)
        {
            return InvoiceApiHelpers.ToFailure(result);
        }

        var report = result.Value!;
        var response = new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            ProviderInvoiceCount = report.ProviderInvoiceCount,
            EShopInvoiceCount = report.EShopInvoiceCount,
            RecordedByBothCount = report.RecordedByBothCount,
            ProviderOnlyCount = report.ProviderOnlyCount,
            EShopOnlyCount = report.EShopOnlyCount,
            Entries = report.Entries.Select(ReconciliationEntryDto.From).ToList()
        };
        return Results.Ok(response);
    }
}

/// <summary>The reconciliation report, with counts and one entry per bill in range.</summary>
public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }
    public ReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int ProviderInvoiceCount { get; set; }
    public int EShopInvoiceCount { get; set; }
    public int RecordedByBothCount { get; set; }
    public int ProviderOnlyCount { get; set; }
    public int EShopOnlyCount { get; set; }
    public List<ReconciliationEntryDto> Entries { get; set; } = new();
}

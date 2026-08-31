using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
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
/// Returns the caller's own bills, each showing where it has got to. Each entry carries its own
/// <c>invoiceId</c>, which is what the operator endpoints act on.
/// </summary>
public class MyInvoicesEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-invoices",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IInvoicingService service, ClaimsPrincipal user, CancellationToken cancellationToken) =>
                await HandleAsync(service, user, cancellationToken))
            .Produces<MyInvoicesResponse>()
            .WithTags("Invoices");
    }

    public async Task<IResult> HandleAsync(IInvoicingService service, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var buyerId = user.Identity?.Name ?? string.Empty;
        var result = await service.GetInvoicesForShopperAsync(buyerId, cancellationToken);
        if (!result.IsSuccess)
        {
            return InvoiceApiHelpers.ToFailure(result);
        }

        var response = new MyInvoicesResponse
        {
            Invoices = result.Value!.Select(InvoiceSummaryDto.From).ToList()
        };
        return Results.Ok(response);
    }
}

/// <summary>Response listing the caller's bills.</summary>
public class MyInvoicesResponse : BaseResponse
{
    public MyInvoicesResponse(Guid correlationId) : base(correlationId) { }
    public MyInvoicesResponse() { }

    public List<InvoiceSummaryDto> Invoices { get; set; } = new();
}

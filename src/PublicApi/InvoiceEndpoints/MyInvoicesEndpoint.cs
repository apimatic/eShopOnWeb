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

/// <summary>The caller's own bills, each showing where it has got to.</summary>
public class MyInvoicesEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-invoices",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                ClaimsPrincipal user,
                IInvoiceService invoiceService,
                CancellationToken cancellationToken) =>
            {
                var invoices = await invoiceService.GetMyInvoicesAsync(user.BuyerId(), cancellationToken);

                var response = new MyInvoicesResponse
                {
                    Invoices = invoices.Select(InvoiceMappings.ToMyInvoiceDto).ToList()
                };

                return Results.Ok(response);
            })
            .Produces<MyInvoicesResponse>()
            .WithTags("InvoiceEndpoints");
    }
}

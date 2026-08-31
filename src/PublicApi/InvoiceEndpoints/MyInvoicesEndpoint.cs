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
/// The caller's own bills, each showing where it has got to. Each entry carries its
/// invoice id, since that is what the operator endpoints act on.
/// </summary>
public class MyInvoicesEndpoint : IEndpoint<IResult, IInvoiceService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MyInvoicesEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-invoices",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IInvoiceService invoiceService) =>
            {
                return await HandleAsync(invoiceService);
            })
            .Produces<MyInvoicesResponse>()
            .WithTags("InvoiceEndpoints");
    }

    public async Task<IResult> HandleAsync(IInvoiceService invoiceService)
    {
        var buyerId = _httpContextAccessor.HttpContext?.User.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var invoices = await invoiceService.GetInvoicesForShopperAsync(buyerId);

        var response = new MyInvoicesResponse
        {
            Invoices = invoices.Select(InvoiceDtoMapper.ToDto).ToList()
        };

        return Results.Ok(response);
    }
}

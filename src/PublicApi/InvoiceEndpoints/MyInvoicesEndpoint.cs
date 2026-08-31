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
/// The caller's own bills, each showing where it has got to. Shopper-scoped: it returns only the caller's data.
/// </summary>
public class MyInvoicesEndpoint : IEndpoint<IResult, MyInvoicesRequest, ClaimsPrincipal>
{
    private readonly IInvoiceService _invoiceService;

    public MyInvoicesEndpoint(IInvoiceService invoiceService)
    {
        _invoiceService = invoiceService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-invoices",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, CancellationToken ct) =>
            {
                return await HandleAsync(new MyInvoicesRequest(), user, ct);
            })
            .Produces<MyInvoicesResponse>()
            .WithTags("InvoiceEndpoints");
    }

    public Task<IResult> HandleAsync(MyInvoicesRequest request, ClaimsPrincipal user) => HandleAsync(request, user, default);

    public async Task<IResult> HandleAsync(MyInvoicesRequest request, ClaimsPrincipal user, CancellationToken ct)
    {
        var buyerId = user.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var response = new MyInvoicesResponse(request.CorrelationId());

        var invoices = await _invoiceService.GetInvoicesForShopperAsync(buyerId, ct);

        response.Invoices = invoices.Select(InvoiceMapping.ToDto).ToList();

        return Results.Ok(response);
    }
}

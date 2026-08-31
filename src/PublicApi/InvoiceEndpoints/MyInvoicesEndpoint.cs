using System.Linq;
using System.Security.Claims;
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
/// Lists the caller's own bills, each showing where it has got to and carrying its own <c>invoiceId</c>.
/// </summary>
public class MyInvoicesEndpoint : IEndpoint<IResult, MyInvoicesRequest, IInvoiceService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-invoices",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IInvoiceService invoiceService) =>
            {
                var buyerId = CallerIdentity.BuyerId(user);
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(new MyInvoicesRequest(buyerId), invoiceService);
            })
            .Produces<MyInvoiceResponse[]>()
            .WithTags("InvoiceEndpoints");
    }

    public async Task<IResult> HandleAsync(MyInvoicesRequest request, IInvoiceService invoiceService)
    {
        var result = await invoiceService.ListMineAsync(request.BuyerId);
        return InvoiceApiResults.ToHttp(result, items => Results.Ok(items.Select(i => new MyInvoiceResponse
        {
            InvoiceId = i.InvoiceId,
            OrderId = i.OrderId,
            State = i.State,
            ProviderStatus = i.ProviderStatus,
            Amount = i.Amount,
            Currency = i.Currency,
            DueDate = i.DueDate
        }).ToList()));
    }
}

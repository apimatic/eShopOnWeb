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
/// Lists the caller's own bills, each showing where it has got to and carrying its <c>invoiceId</c>.
/// </summary>
public class GetMyInvoicesEndpoint : IEndpoint<IResult, MyInvoicesRequest, IInvoiceService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-invoices",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                ClaimsPrincipal user,
                IInvoiceService invoiceService,
                CancellationToken ct) =>
            {
                var buyerId = CallerIdentity.BuyerId(user);
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                return await ExecuteAsync(new MyInvoicesRequest(), buyerId, invoiceService, ct);
            })
            .Produces<MyInvoicesResponse>()
            .WithTags("InvoiceEndpoints");
    }

    public Task<IResult> HandleAsync(MyInvoicesRequest request, IInvoiceService invoiceService) =>
        ExecuteAsync(request, string.Empty, invoiceService, CancellationToken.None);

    private static async Task<IResult> ExecuteAsync(MyInvoicesRequest request, string buyerId,
        IInvoiceService invoiceService, CancellationToken ct)
    {
        var invoices = await invoiceService.GetInvoicesForBuyerAsync(buyerId, ct);

        var response = new MyInvoicesResponse(request.CorrelationId())
        {
            Invoices = invoices.Select(i => new MyInvoiceItem
            {
                InvoiceId = i.InvoiceId,
                OrderId = i.OrderId,
                LocalStatus = i.LocalStatus,
                DueDate = i.DueDate,
                Amount = i.Amount,
                Currency = i.Currency
            }).ToList()
        };

        return Results.Ok(response);
    }
}

using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>
/// Reads one of the shopper's bills: its current state, what the provider reports about how it got
/// there, and — once issued and still payable — how to pay it (top-level <c>paymentLink</c>).
/// A shopper can only read their own bill.
/// </summary>
public class GetInvoiceEndpoint : IEndpoint<IResult, GetInvoiceRequest, IInvoiceService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/invoices/{invoiceId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                string invoiceId,
                ClaimsPrincipal user,
                IInvoiceService invoiceService,
                CancellationToken ct) =>
            {
                var buyerId = CallerIdentity.BuyerId(user);
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                return await ExecuteAsync(new GetInvoiceRequest(invoiceId), buyerId, invoiceService, ct);
            })
            .Produces<InvoiceResponse>()
            .WithTags("InvoiceEndpoints");
    }

    public Task<IResult> HandleAsync(GetInvoiceRequest request, IInvoiceService invoiceService) =>
        ExecuteAsync(request, string.Empty, invoiceService, CancellationToken.None);

    private static async Task<IResult> ExecuteAsync(GetInvoiceRequest request, string buyerId,
        IInvoiceService invoiceService, CancellationToken ct)
    {
        var result = await invoiceService.GetInvoiceAsync(request.InvoiceId, buyerId, ct);

        return result.Outcome switch
        {
            ServiceOutcome.Ok => Results.Ok(InvoiceResponse.From(result.Value!, request.CorrelationId())),
            _ => Results.NotFound(result.Error)
        };
    }
}

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
/// Raises a bill with the provider for one of the caller's own orders. What is billed comes from the
/// order itself — its items and what they cost — not from anything the caller restates. The bill
/// starts out not yet put to the shopper.
/// </summary>
public class RaiseInvoiceEndpoint : IEndpoint<IResult, RaiseInvoiceRequest, IInvoiceService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/invoice",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RaiseInvoiceRequest request, ClaimsPrincipal user, IInvoiceService invoiceService) =>
            {
                request.OrderId = orderId;
                request.BuyerId = user.GetBuyerId();
                return await HandleAsync(request, invoiceService);
            })
            .Produces<RaiseInvoiceResponse>(StatusCodes.Status201Created)
            .WithTags("InvoiceEndpoints");
    }

    public async Task<IResult> HandleAsync(RaiseInvoiceRequest request, IInvoiceService invoiceService)
    {
        var invoice = await invoiceService.RaiseInvoiceAsync(request.OrderId, request.BuyerId, request.DueDate);

        var response = new RaiseInvoiceResponse(request.CorrelationId())
        {
            InvoiceId = invoice.Id,
            OrderId = invoice.OrderId,
            ProviderInvoiceId = invoice.ProviderInvoiceId,
            InvoiceNumber = invoice.InvoiceNumber,
            Status = invoice.Status.ToString(),
            Amount = invoice.Amount,
            Currency = invoice.CurrencyCode,
            DueDate = invoice.DueDate
        };

        return Results.Created($"api/invoices/{invoice.Id}", response);
    }
}

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
/// Reads a bill's current state, whatever the provider reports about how it reached that
/// state, and — once it has been put to the shopper — how they can pay it. A shopper may
/// only read their own bill; an operator may read any.
/// </summary>
public class GetInvoiceEndpoint : IEndpoint<IResult, GetInvoiceRequest, IInvoiceService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public GetInvoiceEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/invoices/{invoiceId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string invoiceId, IInvoiceService invoiceService) =>
            {
                return await HandleAsync(new GetInvoiceRequest { InvoiceId = invoiceId }, invoiceService);
            })
            .Produces<GetInvoiceResponse>()
            .WithTags("InvoiceEndpoints");
    }

    public async Task<IResult> HandleAsync(GetInvoiceRequest request, IInvoiceService invoiceService)
    {
        var user = _httpContextAccessor.HttpContext?.User;
        var buyerId = user?.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var result = await invoiceService.GetInvoiceAsync(request.InvoiceId, buyerId, user!.IsOperator());

        return ApiResults.From(result, view =>
        {
            var response = new GetInvoiceResponse(request.CorrelationId())
            {
                Invoice = InvoiceDtoMapper.ToDto(view),
                PaymentLink = view.PaymentLink
            };
            return Results.Ok(response);
        });
    }
}

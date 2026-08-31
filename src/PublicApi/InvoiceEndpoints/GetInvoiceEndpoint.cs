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
/// Reads one of the shopper's own bills: its current state, the provider's account of how it reached that
/// state, and — once it has been put to the shopper — how they can pay it.
/// </summary>
public class GetInvoiceEndpoint : IEndpoint<IResult, GetInvoiceRequest, ClaimsPrincipal>
{
    private readonly IInvoiceService _invoiceService;

    public GetInvoiceEndpoint(IInvoiceService invoiceService)
    {
        _invoiceService = invoiceService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/invoices/{invoiceId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int invoiceId, ClaimsPrincipal user, CancellationToken ct) =>
            {
                return await HandleAsync(new GetInvoiceRequest(invoiceId), user, ct);
            })
            .Produces<GetInvoiceResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("InvoiceEndpoints");
    }

    public Task<IResult> HandleAsync(GetInvoiceRequest request, ClaimsPrincipal user) => HandleAsync(request, user, default);

    public async Task<IResult> HandleAsync(GetInvoiceRequest request, ClaimsPrincipal user, CancellationToken ct)
    {
        var buyerId = user.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var response = new GetInvoiceResponse(request.CorrelationId());

        var details = await _invoiceService.GetInvoiceForShopperAsync(request.InvoiceId, buyerId, ct);

        response.Invoice = InvoiceMapping.ToDto(details.Invoice);
        response.ProviderStatus = details.ProviderStatus;
        response.PaymentLink = details.PaymentLink;
        response.History = details.History.Select(InvoiceMapping.ToDto).ToList();

        return Results.Ok(response);
    }
}

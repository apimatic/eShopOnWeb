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
/// Returns a bill's current state, the provider's account of how it reached that state, and — once it
/// has been put to the shopper — how it can be paid (a top-level payment link). Scoped to the caller's
/// own bills; operators may read any.
/// </summary>
public class GetInvoiceEndpoint : IEndpoint<IResult, GetInvoiceRequest, IInvoiceService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/invoices/{invoiceId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string invoiceId, IInvoiceService invoiceService, ClaimsPrincipal user) =>
            {
                return await HandleAsync(new GetInvoiceRequest { InvoiceId = invoiceId }, invoiceService, user);
            })
            .Produces<InvoiceResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("InvoiceEndpoints");
    }

    public async Task<IResult> HandleAsync(
        GetInvoiceRequest request,
        IInvoiceService invoiceService,
        ClaimsPrincipal user)
    {
        var details = await invoiceService.GetInvoiceAsync(user.GetBuyerId(), user.IsAdministrator(), request.InvoiceId);
        return Results.Ok(InvoiceDtoMapper.ToResponse(details, request.CorrelationId()));
    }
}

public class GetInvoiceRequest : BaseRequest
{
    public string InvoiceId { get; set; } = string.Empty;
}

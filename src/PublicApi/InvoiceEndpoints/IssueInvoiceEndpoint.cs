using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

public class IssueInvoiceRequest : BaseRequest
{
    public IssueInvoiceRequest(string invoiceId) => InvoiceId = invoiceId;
    public string InvoiceId { get; }
}

/// <summary>
/// Puts a bill to the shopper. Afterwards a way to pay it can be handed out and the bill reports
/// itself as having been put to them. Operator action — restricted to the administrator role; it
/// may act on any shopper's bill.
/// </summary>
public class IssueInvoiceEndpoint : IEndpoint<IResult, IssueInvoiceRequest, IInvoiceService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/invoices/{invoiceId}/issue",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                       AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string invoiceId, IInvoiceService invoiceService) =>
            {
                return await HandleAsync(new IssueInvoiceRequest(invoiceId), invoiceService);
            })
            .Produces<InvoiceResponse>()
            .WithTags("InvoiceEndpoints");
    }

    public async Task<IResult> HandleAsync(IssueInvoiceRequest request, IInvoiceService invoiceService)
    {
        var detail = await invoiceService.IssueInvoiceAsync(request.InvoiceId);
        return Results.Ok(InvoiceResponse.From(detail, request.CorrelationId()));
    }
}

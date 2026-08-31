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
/// Operator action: puts the bill to the shopper. Afterwards the application can hand out a way to pay
/// it and the bill reports itself as having been put to them. Restricted to the administrator role.
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
                return await HandleAsync(new IssueInvoiceRequest { InvoiceId = invoiceId }, invoiceService);
            })
            .Produces<InvoiceResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("InvoiceEndpoints");
    }

    public async Task<IResult> HandleAsync(IssueInvoiceRequest request, IInvoiceService invoiceService)
    {
        var details = await invoiceService.IssueInvoiceAsync(request.InvoiceId);
        return Results.Ok(InvoiceDtoMapper.ToResponse(details, request.CorrelationId()));
    }
}

public class IssueInvoiceRequest : BaseRequest
{
    public string InvoiceId { get; set; } = string.Empty;
}

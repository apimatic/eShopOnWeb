using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

public class IssueInvoiceResponse : BaseResponse
{
    public IssueInvoiceResponse(Guid correlationId) : base(correlationId) { }
    public IssueInvoiceResponse() { }

    public InvoiceDto Invoice { get; set; } = new();
}

/// <summary>
/// Operator action: put a bill to the shopper. Afterwards the application can hand out a way to pay it and
/// the bill reports itself as having been put to them. Restricted to the administrator role.
/// </summary>
public class IssueInvoiceEndpoint : IEndpoint<IResult, string>
{
    private readonly IInvoiceService _invoiceService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public IssueInvoiceEndpoint(IInvoiceService invoiceService, IHttpContextAccessor httpContextAccessor)
    {
        _invoiceService = invoiceService;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/invoices/{invoiceId}/issue",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string invoiceId) =>
            {
                return await HandleAsync(invoiceId);
            })
            .Produces<IssueInvoiceResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("InvoiceEndpoints");
    }

    public async Task<IResult> HandleAsync(string invoiceId)
    {
        var context = _httpContextAccessor.HttpContext!;
        var invoice = await _invoiceService.IssueInvoiceAsync(invoiceId, context.RequestAborted);
        if (invoice is null)
            return Results.NotFound();

        return Results.Ok(new IssueInvoiceResponse { Invoice = InvoiceDto.From(invoice) });
    }
}

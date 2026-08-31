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

public class WithdrawInvoiceResponse : BaseResponse
{
    public WithdrawInvoiceResponse(Guid correlationId) : base(correlationId) { }
    public WithdrawInvoiceResponse() { }

    public InvoiceDto Invoice { get; set; } = new();
}

/// <summary>
/// Operator action: withdraw a bill that should not be paid. Afterwards it is no longer payable and no
/// payment link is handed out. Restricted to the administrator role.
/// </summary>
public class WithdrawInvoiceEndpoint : IEndpoint<IResult, string>
{
    private readonly IInvoiceService _invoiceService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public WithdrawInvoiceEndpoint(IInvoiceService invoiceService, IHttpContextAccessor httpContextAccessor)
    {
        _invoiceService = invoiceService;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/invoices/{invoiceId}/withdraw",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string invoiceId) =>
            {
                return await HandleAsync(invoiceId);
            })
            .Produces<WithdrawInvoiceResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("InvoiceEndpoints");
    }

    public async Task<IResult> HandleAsync(string invoiceId)
    {
        var context = _httpContextAccessor.HttpContext!;
        var invoice = await _invoiceService.WithdrawInvoiceAsync(invoiceId, context.RequestAborted);
        if (invoice is null)
            return Results.NotFound();

        return Results.Ok(new WithdrawInvoiceResponse { Invoice = InvoiceDto.From(invoice) });
    }
}

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
/// Operator action: withdraw a bill that should not be paid. Afterwards it is no longer payable and no
/// payment link is handed out. Restricted to the administrator role.
/// </summary>
public class WithdrawInvoiceEndpoint : IEndpoint<IResult, WithdrawInvoiceRequest, ClaimsPrincipal>
{
    private readonly IInvoiceService _invoiceService;

    public WithdrawInvoiceEndpoint(IInvoiceService invoiceService)
    {
        _invoiceService = invoiceService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/invoices/{invoiceId}/withdraw",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int invoiceId, ClaimsPrincipal user, CancellationToken ct) =>
            {
                return await HandleAsync(new WithdrawInvoiceRequest(invoiceId), user, ct);
            })
            .Produces<InvoiceActionResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("InvoiceEndpoints");
    }

    public Task<IResult> HandleAsync(WithdrawInvoiceRequest request, ClaimsPrincipal user) => HandleAsync(request, user, default);

    public async Task<IResult> HandleAsync(WithdrawInvoiceRequest request, ClaimsPrincipal user, CancellationToken ct)
    {
        var response = new InvoiceActionResponse(request.CorrelationId());

        var invoice = await _invoiceService.WithdrawInvoiceAsync(request.InvoiceId, ct);

        response.Invoice = InvoiceMapping.ToDto(invoice);

        return Results.Ok(response);
    }
}

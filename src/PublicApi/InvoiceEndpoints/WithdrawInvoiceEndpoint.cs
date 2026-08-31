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
/// Operator action: withdraws a bill that should not be paid. Afterwards it is no longer payable and
/// the way to pay it is no longer handed out. Restricted to the administrator role.
/// </summary>
public class WithdrawInvoiceEndpoint : IEndpoint<IResult, WithdrawInvoiceRequest, IInvoiceService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/invoices/{invoiceId}/withdraw",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                       AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string invoiceId, IInvoiceService invoiceService) =>
            {
                return await HandleAsync(new WithdrawInvoiceRequest { InvoiceId = invoiceId }, invoiceService);
            })
            .Produces<InvoiceResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("InvoiceEndpoints");
    }

    public async Task<IResult> HandleAsync(WithdrawInvoiceRequest request, IInvoiceService invoiceService)
    {
        var details = await invoiceService.WithdrawInvoiceAsync(request.InvoiceId);
        return Results.Ok(InvoiceDtoMapper.ToResponse(details, request.CorrelationId()));
    }
}

public class WithdrawInvoiceRequest : BaseRequest
{
    public string InvoiceId { get; set; } = string.Empty;
}

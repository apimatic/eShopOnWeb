using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

public class WithdrawInvoiceRequest : BaseRequest
{
    public WithdrawInvoiceRequest(string invoiceId) => InvoiceId = invoiceId;
    public string InvoiceId { get; }
}

/// <summary>
/// Withdraws a bill that should not be paid. Afterwards it is no longer payable and the way to pay
/// it is no longer handed out. Operator action — restricted to the administrator role; it may act
/// on any shopper's bill.
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
                return await HandleAsync(new WithdrawInvoiceRequest(invoiceId), invoiceService);
            })
            .Produces<InvoiceResponse>()
            .WithTags("InvoiceEndpoints");
    }

    public async Task<IResult> HandleAsync(WithdrawInvoiceRequest request, IInvoiceService invoiceService)
    {
        var detail = await invoiceService.WithdrawInvoiceAsync(request.InvoiceId);
        return Results.Ok(InvoiceResponse.From(detail, request.CorrelationId()));
    }
}

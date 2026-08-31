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
/// Operator action: withdraws a bill that should not be paid. Afterwards it is no longer payable and
/// the way to pay it is no longer handed out. Restricted to the administrator role.
/// </summary>
public class WithdrawInvoiceEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/invoices/{invoiceId}/withdraw",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string invoiceId, IInvoicingService service, CancellationToken cancellationToken) =>
                await HandleAsync(invoiceId, service, cancellationToken))
            .Produces<InvoiceDetailsResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("Invoices");
    }

    public async Task<IResult> HandleAsync(string invoiceId, IInvoicingService service, CancellationToken cancellationToken)
    {
        var result = await service.WithdrawInvoiceAsync(invoiceId, cancellationToken);
        if (!result.IsSuccess)
        {
            return InvoiceApiHelpers.ToFailure(result);
        }
        return Results.Ok(InvoiceDetailsResponse.From(result.Value!, System.Guid.NewGuid()));
    }
}

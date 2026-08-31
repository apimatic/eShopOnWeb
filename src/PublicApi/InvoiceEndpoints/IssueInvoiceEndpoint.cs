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
/// Puts the bill to the shopper (operator action). Afterwards the application can hand out a way to pay
/// it and the bill reports itself as having been put to them.
/// </summary>
public class IssueInvoiceEndpoint : IEndpoint
{
    private readonly IInvoiceManagementService _invoiceService;

    public IssueInvoiceEndpoint(IInvoiceManagementService invoiceService)
    {
        _invoiceService = invoiceService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/invoices/{invoiceId}/issue",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string invoiceId) =>
            {
                return await HandleAsync(invoiceId);
            })
            .Produces<InvoiceResponse>()
            .WithTags("InvoiceEndpoints");
    }

    public async Task<IResult> HandleAsync(string invoiceId)
    {
        var snapshot = await _invoiceService.IssueInvoiceAsync(invoiceId);
        return Results.Ok(InvoiceResponse.From(snapshot));
    }
}

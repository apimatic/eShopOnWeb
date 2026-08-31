using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

public class MyInvoicesResponse : BaseResponse
{
    public List<InvoiceDto> Invoices { get; set; } = new();
}

/// <summary>The caller's own bills, each showing where it has got to.</summary>
public class MyInvoicesEndpoint : IEndpoint<IResult>
{
    private readonly IInvoiceService _invoiceService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MyInvoicesEndpoint(IInvoiceService invoiceService, IHttpContextAccessor httpContextAccessor)
    {
        _invoiceService = invoiceService;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-invoices",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async () =>
            {
                return await HandleAsync();
            })
            .Produces<MyInvoicesResponse>()
            .WithTags("InvoiceEndpoints");
    }

    public async Task<IResult> HandleAsync()
    {
        var context = _httpContextAccessor.HttpContext!;
        var buyerId = context.User?.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        var invoices = await _invoiceService.GetInvoicesForBuyerAsync(buyerId, context.RequestAborted);

        var response = new MyInvoicesResponse
        {
            Invoices = invoices.Select(InvoiceDto.From).ToList(),
        };
        return Results.Ok(response);
    }
}

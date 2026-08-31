using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

public class MyInvoicesResponse : BaseResponse
{
    public MyInvoicesResponse(Guid correlationId) : base(correlationId)
    {
    }

    public MyInvoicesResponse()
    {
    }

    public List<InvoiceListItemDto> Invoices { get; set; } = new();
}

/// <summary>Returns the caller's own bills, each showing where it has got to.</summary>
public class MyInvoicesEndpoint : IEndpoint<IResult, string, IInvoiceService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-invoices",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IInvoiceService invoiceService) =>
            {
                return await HandleAsync(user.GetBuyerId(), invoiceService);
            })
            .Produces<MyInvoicesResponse>()
            .WithTags("InvoiceEndpoints");
    }

    public async Task<IResult> HandleAsync(string buyerId, IInvoiceService invoiceService)
    {
        var invoices = await invoiceService.GetMyInvoicesAsync(buyerId);
        var response = new MyInvoicesResponse(Guid.NewGuid())
        {
            Invoices = invoices.Select(InvoiceMapping.ToListItem).ToList()
        };
        return Results.Ok(response);
    }
}

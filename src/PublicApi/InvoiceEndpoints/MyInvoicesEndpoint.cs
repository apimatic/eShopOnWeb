using System;
using System.Collections.Generic;
using System.Linq;
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
/// Lists the caller's own bills, each showing where it has got to (and carrying its own invoiceId,
/// which the operator endpoints act on).
/// </summary>
public class MyInvoicesEndpoint : IEndpoint<IResult, MyInvoicesRequest, IInvoicingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-invoices",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IInvoicingService service, CancellationToken ct) =>
            {
                var buyerId = CallerIdentity.GetUserName(user);
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();

                return await HandleAsync(new MyInvoicesRequest(buyerId), service, ct);
            })
            .Produces<MyInvoicesResponse>()
            .WithTags("InvoiceEndpoints");
    }

    public Task<IResult> HandleAsync(MyInvoicesRequest request, IInvoicingService service) =>
        HandleAsync(request, service, CancellationToken.None);

    public async Task<IResult> HandleAsync(MyInvoicesRequest request, IInvoicingService service, CancellationToken ct)
    {
        return await InvoicingProblem.GuardAsync(async () =>
        {
            var invoices = await service.GetInvoicesForShopperAsync(request.BuyerId, ct);
            var response = new MyInvoicesResponse(request.CorrelationId())
            {
                Invoices = invoices.Select(InvoiceViewMapper.ToView).ToList(),
            };
            return Results.Ok(response);
        });
    }
}

public class MyInvoicesRequest : BaseRequest
{
    public string BuyerId { get; }

    public MyInvoicesRequest(string buyerId) => BuyerId = buyerId;
}

public class MyInvoicesResponse : BaseResponse
{
    public MyInvoicesResponse(Guid correlationId) : base(correlationId) { }

    public MyInvoicesResponse() { }

    public List<InvoiceSummaryView> Invoices { get; set; } = new();
}

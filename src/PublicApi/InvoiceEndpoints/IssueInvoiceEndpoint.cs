using System;
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
/// Operator action: puts the bill to the shopper. Afterwards a pay link can be handed out and the bill
/// reports itself as issued. Restricted to the administrator role.
/// </summary>
public class IssueInvoiceEndpoint : IEndpoint<IResult, IssueInvoiceRequest, IInvoicingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/invoices/{invoiceId}/issue",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string invoiceId, IInvoicingService service, CancellationToken ct) =>
            {
                return await HandleAsync(new IssueInvoiceRequest(invoiceId), service, ct);
            })
            .Produces<IssueInvoiceResponse>()
            .WithTags("InvoiceEndpoints");
    }

    public Task<IResult> HandleAsync(IssueInvoiceRequest request, IInvoicingService service) =>
        HandleAsync(request, service, CancellationToken.None);

    public async Task<IResult> HandleAsync(IssueInvoiceRequest request, IInvoicingService service, CancellationToken ct)
    {
        return await InvoicingProblem.GuardAsync(async () =>
        {
            await service.IssueInvoiceAsync(request.InvoiceId, ct);
            var response = new IssueInvoiceResponse(request.CorrelationId())
            {
                InvoiceId = request.InvoiceId,
                Status = "Issued",
            };
            return Results.Ok(response);
        });
    }
}

public class IssueInvoiceRequest : BaseRequest
{
    public string InvoiceId { get; }

    public IssueInvoiceRequest(string invoiceId) => InvoiceId = invoiceId;
}

public class IssueInvoiceResponse : BaseResponse
{
    public IssueInvoiceResponse(Guid correlationId) : base(correlationId) { }

    public IssueInvoiceResponse() { }

    public string InvoiceId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

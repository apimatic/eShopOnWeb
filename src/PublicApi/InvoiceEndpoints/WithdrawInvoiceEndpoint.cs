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
/// Operator action: withdraws a bill that should not be paid. Afterwards it is no longer payable and
/// the way to pay it is no longer handed out. Restricted to the administrator role.
/// </summary>
public class WithdrawInvoiceEndpoint : IEndpoint<IResult, WithdrawInvoiceRequest, IInvoicingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/invoices/{invoiceId}/withdraw",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string invoiceId, IInvoicingService service, CancellationToken ct) =>
            {
                return await HandleAsync(new WithdrawInvoiceRequest(invoiceId), service, ct);
            })
            .Produces<WithdrawInvoiceResponse>()
            .WithTags("InvoiceEndpoints");
    }

    public Task<IResult> HandleAsync(WithdrawInvoiceRequest request, IInvoicingService service) =>
        HandleAsync(request, service, CancellationToken.None);

    public async Task<IResult> HandleAsync(WithdrawInvoiceRequest request, IInvoicingService service, CancellationToken ct)
    {
        return await InvoicingProblem.GuardAsync(async () =>
        {
            await service.WithdrawInvoiceAsync(request.InvoiceId, ct);
            var response = new WithdrawInvoiceResponse(request.CorrelationId())
            {
                InvoiceId = request.InvoiceId,
                Status = "Withdrawn",
            };
            return Results.Ok(response);
        });
    }
}

public class WithdrawInvoiceRequest : BaseRequest
{
    public string InvoiceId { get; }

    public WithdrawInvoiceRequest(string invoiceId) => InvoiceId = invoiceId;
}

public class WithdrawInvoiceResponse : BaseResponse
{
    public WithdrawInvoiceResponse(Guid correlationId) : base(correlationId) { }

    public WithdrawInvoiceResponse() { }

    public string InvoiceId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

using System;
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
/// Corrects the due date and/or customer details of one of the shopper's own draft bills. Once the bill has
/// been put to the shopper or withdrawn, the correction is refused (409) rather than silently doing nothing.
/// </summary>
public class UpdateInvoiceEndpoint : IEndpoint<IResult, UpdateInvoiceRequest, ClaimsPrincipal>
{
    private readonly IInvoiceService _invoiceService;

    public UpdateInvoiceEndpoint(IInvoiceService invoiceService)
    {
        _invoiceService = invoiceService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPatch("api/invoices/{invoiceId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int invoiceId, UpdateInvoiceRequest request, ClaimsPrincipal user, CancellationToken ct) =>
            {
                request.InvoiceId = invoiceId;
                return await HandleAsync(request, user, ct);
            })
            .Produces<UpdateInvoiceResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("InvoiceEndpoints");
    }

    public Task<IResult> HandleAsync(UpdateInvoiceRequest request, ClaimsPrincipal user) => HandleAsync(request, user, default);

    public async Task<IResult> HandleAsync(UpdateInvoiceRequest request, ClaimsPrincipal user, CancellationToken ct)
    {
        var buyerId = user.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var response = new UpdateInvoiceResponse(request.CorrelationId());

        DateTimeOffset? dueDate = request.DueDate.HasValue
            ? new DateTimeOffset(request.DueDate.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
            : null;

        var correction = new InvoiceCorrectionRequest(dueDate, request.CustomerName, request.CustomerEmail);

        var invoice = await _invoiceService.CorrectInvoiceAsync(request.InvoiceId, buyerId, correction, ct);

        response.Invoice = InvoiceMapping.ToDto(invoice);

        return Results.Ok(response);
    }
}

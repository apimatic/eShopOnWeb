using System;
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

/// <summary>
/// Corrects the due date and/or the customer details a bill carries, while it has not yet been put to
/// the shopper. What is billed still comes from the order, so the amount is not correctable here. Once
/// the bill has been put to the shopper or withdrawn, correcting it is refused (409) rather than
/// silently doing nothing. Scoped to the caller's own bills; operators may correct any.
/// </summary>
public class UpdateInvoiceEndpoint : IEndpoint<IResult, UpdateInvoiceRequest, IInvoiceService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapMethods("api/invoices/{invoiceId}", new[] { "PATCH" },
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string invoiceId, UpdateInvoiceRequest request, IInvoiceService invoiceService, ClaimsPrincipal user) =>
            {
                request.InvoiceId = invoiceId;
                return await HandleAsync(request, invoiceService, user);
            })
            .Produces<InvoiceResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("InvoiceEndpoints");
    }

    public async Task<IResult> HandleAsync(
        UpdateInvoiceRequest request,
        IInvoiceService invoiceService,
        ClaimsPrincipal user)
    {
        var details = await invoiceService.CorrectInvoiceAsync(
            user.GetBuyerId(), user.IsAdministrator(), request.InvoiceId,
            request.DueDate, request.CustomerName, request.CustomerEmail);

        return Results.Ok(InvoiceDtoMapper.ToResponse(details, request.CorrelationId()));
    }
}

public class UpdateInvoiceRequest : BaseRequest
{
    public string InvoiceId { get; set; } = string.Empty;

    /// <summary>New due date, if being corrected.</summary>
    public DateTime? DueDate { get; set; }

    public string? CustomerName { get; set; }
    public string? CustomerEmail { get; set; }
}

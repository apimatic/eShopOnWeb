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
/// Raises a bill with the provider for one of the shopper's own orders. What is billed comes from the
/// order itself; the request carries only the due date. The bill starts out not yet put to the shopper.
/// </summary>
public class CreateInvoiceForOrderEndpoint : IEndpoint<IResult, CreateInvoiceForOrderRequest, ClaimsPrincipal>
{
    private readonly IInvoiceService _invoiceService;

    public CreateInvoiceForOrderEndpoint(IInvoiceService invoiceService)
    {
        _invoiceService = invoiceService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/invoice",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, CreateInvoiceForOrderRequest request, ClaimsPrincipal user, CancellationToken ct) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, user, ct);
            })
            .Produces<CreateInvoiceResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("InvoiceEndpoints");
    }

    public Task<IResult> HandleAsync(CreateInvoiceForOrderRequest request, ClaimsPrincipal user) => HandleAsync(request, user, default);

    public async Task<IResult> HandleAsync(CreateInvoiceForOrderRequest request, ClaimsPrincipal user, CancellationToken ct)
    {
        var buyerId = user.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var response = new CreateInvoiceResponse(request.CorrelationId());

        var dueDate = new DateTimeOffset(request.DueDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var invoice = await _invoiceService.RaiseInvoiceForOrderAsync(request.OrderId, buyerId, dueDate, ct);

        response.InvoiceId = invoice.Id;
        response.Status = invoice.Status.ToString();
        response.ProviderInvoiceId = invoice.ProviderInvoiceId;

        return Results.Created($"api/invoices/{invoice.Id}", response);
    }
}

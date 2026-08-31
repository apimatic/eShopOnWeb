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
/// Raises a bill with the provider for one of the caller's orders. What is billed comes from the order
/// itself (items and their cost), never from the caller; the request carries only the date the bill
/// falls due. The bill starts out not yet put to the shopper.
/// </summary>
public class CreateInvoiceForOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/invoice",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, CreateInvoiceForOrderRequest request, IInvoicingService service, ClaimsPrincipal user, CancellationToken cancellationToken) =>
                await HandleAsync(orderId, request, service, user, cancellationToken))
            .Produces<RaiseInvoiceResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("Invoices");
    }

    public async Task<IResult> HandleAsync(int orderId, CreateInvoiceForOrderRequest request, IInvoicingService service, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var caller = InvoiceApiHelpers.GetCaller(user);
        var result = await service.RaiseInvoiceAsync(orderId, request.DueDate, caller, cancellationToken);
        if (!result.IsSuccess)
        {
            return InvoiceApiHelpers.ToFailure(result);
        }

        var raised = result.Value!;
        var response = new RaiseInvoiceResponse(request.CorrelationId())
        {
            InvoiceId = raised.InvoiceId,
            OrderId = raised.OrderId,
            Status = raised.Status,
            Amount = raised.Amount,
            Currency = raised.Currency,
            DueDate = raised.DueDate
        };
        return Results.Created($"api/invoices/{raised.InvoiceId}", response);
    }
}

/// <summary>Request body for raising a bill: only the calendar date the bill falls due.</summary>
public class CreateInvoiceForOrderRequest : BaseRequest
{
    public DateOnly DueDate { get; set; }
}

/// <summary>Response for a raised bill. <see cref="InvoiceId"/> is the top-level provider identifier.</summary>
public class RaiseInvoiceResponse : BaseResponse
{
    public RaiseInvoiceResponse(Guid correlationId) : base(correlationId) { }
    public RaiseInvoiceResponse() { }

    public string InvoiceId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateOnly DueDate { get; set; }
}

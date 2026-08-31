using System;
using System.Security.Claims;
using System.Text.Json.Serialization;
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
/// Raises a bill with the provider for the caller's order. What is billed comes from the order itself;
/// the request carries only the calendar date the bill falls due. The bill starts out not yet put to
/// the shopper.
/// </summary>
public class CreateInvoiceEndpoint : IEndpoint<IResult, CreateInvoiceRequest, IInvoicingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/invoice",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, CreateInvoiceRequest request, ClaimsPrincipal user, IInvoicingService service, CancellationToken ct) =>
            {
                var buyerId = CallerIdentity.GetUserName(user);
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();

                request.OrderId = orderId;
                request.BuyerId = buyerId;
                return await HandleAsync(request, service, ct);
            })
            .Produces<CreateInvoiceResponse>(StatusCodes.Status201Created)
            .WithTags("InvoiceEndpoints");
    }

    public Task<IResult> HandleAsync(CreateInvoiceRequest request, IInvoicingService service) =>
        HandleAsync(request, service, CancellationToken.None);

    public async Task<IResult> HandleAsync(CreateInvoiceRequest request, IInvoicingService service, CancellationToken ct)
    {
        return await InvoicingProblem.GuardAsync(async () =>
        {
            var invoiceId = await service.RaiseInvoiceForOrderAsync(request.OrderId, request.DueDate, request.BuyerId, ct);
            var response = new CreateInvoiceResponse(request.CorrelationId())
            {
                InvoiceId = invoiceId,
                OrderId = request.OrderId,
                Status = "Draft",
            };
            return Results.Created($"api/invoices/{invoiceId}", response);
        });
    }
}

public class CreateInvoiceRequest : BaseRequest
{
    /// <summary>The calendar date the bill falls due (ISO-8601 date, e.g. 2026-09-30).</summary>
    public DateOnly DueDate { get; set; }

    // Server-populated from the route and token; never bound from the request body.
    [JsonIgnore] public int OrderId { get; set; }
    [JsonIgnore] public string BuyerId { get; set; } = string.Empty;
}

public class CreateInvoiceResponse : BaseResponse
{
    public CreateInvoiceResponse(Guid correlationId) : base(correlationId) { }

    public CreateInvoiceResponse() { }

    public string InvoiceId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

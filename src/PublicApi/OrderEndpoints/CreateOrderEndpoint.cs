using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order from catalog items at current catalog prices. The order starts
/// in a state awaiting payment.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, HttpContext httpContext, IPaymentService paymentService, CancellationToken cancellationToken) =>
            {
                request.BuyerId = httpContext.User.Identity?.Name ?? string.Empty;
                return await HandleAsync(request, paymentService, cancellationToken);
            })
            .Produces<CreateOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IPaymentService paymentService)
    {
        return await HandleAsync(request, paymentService, CancellationToken.None);
    }

    private async Task<IResult> HandleAsync(CreateOrderRequest request, IPaymentService paymentService, CancellationToken cancellationToken)
    {
        if (request.Items.Count == 0)
        {
            throw new PaymentConflictException("An order needs at least one item.");
        }

        var address = new Address(request.ShipToAddress.Street, request.ShipToAddress.City,
            request.ShipToAddress.State, request.ShipToAddress.Country, request.ShipToAddress.ZipCode);

        var items = request.Items
            .Select(i => new OrderItemRequest(i.CatalogItemId, i.Quantity))
            .ToList();

        var order = await paymentService.CreateOrderAsync(request.BuyerId, items, address, cancellationToken);

        return Results.Ok(new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total(),
            Items = order.OrderItems.Select(i => new CreateOrderItemResponse
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList()
        });
    }
}

public class CreateOrderRequest : BaseRequest
{
    public List<CreateOrderItemRequest> Items { get; set; } = new();
    public ShipToAddressRequest ShipToAddress { get; set; } = new();
    public string BuyerId { get; set; } = string.Empty;
}

public class CreateOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShipToAddressRequest
{
    [Required] public string Street { get; set; } = string.Empty;
    [Required] public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    [Required] public string Country { get; set; } = string.Empty;
    [Required] public string ZipCode { get; set; } = string.Empty;
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId) { }
    public CreateOrderResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public List<CreateOrderItemResponse> Items { get; set; } = new();
}

public class CreateOrderItemResponse
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

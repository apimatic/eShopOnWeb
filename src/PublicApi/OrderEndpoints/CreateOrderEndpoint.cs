using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest : BaseRequest
{
    public List<OrderItemRequestDto> Items { get; set; } = new();
    public AddressDto? ShipToAddress { get; set; }
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId) { }
    public CreateOrderResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = "";
    public decimal Total { get; set; }
    public string Currency { get; set; } = "";
    public List<OrderItemDto> Items { get; set; } = new();
}

/// <summary>
/// Places an order from catalog items. The order starts awaiting payment.
/// </summary>
public class CreateOrderEndpoint : IEndpoint
{
    private static readonly Address DefaultAddress = new("123 Main St", "Kent", "OH", "USA", "44240");

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            ([FromBody] CreateOrderRequest request, HttpContext httpContext,
                IOrderPaymentService orderPaymentService, IPaymentGateway paymentGateway,
                CancellationToken cancellationToken) =>
            {
                return await HandleAsync(request, httpContext, orderPaymentService, paymentGateway, cancellationToken);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, HttpContext httpContext,
        IOrderPaymentService orderPaymentService, IPaymentGateway paymentGateway,
        CancellationToken cancellationToken)
    {
        var buyerId = httpContext.User.GetBuyerId();

        var address = request.ShipToAddress == null
            ? DefaultAddress
            : new Address(
                request.ShipToAddress.Street ?? DefaultAddress.Street,
                request.ShipToAddress.City ?? DefaultAddress.City,
                request.ShipToAddress.State ?? DefaultAddress.State,
                request.ShipToAddress.Country ?? DefaultAddress.Country,
                request.ShipToAddress.ZipCode ?? DefaultAddress.ZipCode);

        var order = await orderPaymentService.CreateOrderAsync(buyerId, address,
            request.Items.Select(i => new OrderItemRequest(i.CatalogItemId, i.Quantity)).ToList(),
            cancellationToken);

        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total(),
            Currency = paymentGateway.Currency,
            Items = order.OrderItems.Select(i => new OrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList()
        };

        return Results.Created($"api/orders/{order.Id}", response);
    }
}

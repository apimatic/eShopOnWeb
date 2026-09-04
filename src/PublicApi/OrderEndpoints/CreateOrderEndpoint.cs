using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequestItem
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderRequest : BaseRequest
{
    public List<CreateOrderRequestItem> Items { get; set; } = new();
    public ShippingAddressDto? ShipToAddress { get; set; }
}

public class ShippingAddressDto
{
    public string Street { get; set; } = "123 Default Street";
    public string City { get; set; } = "Springfield";
    public string State { get; set; } = "ST";
    public string Country { get; set; } = "US";
    public string ZipCode { get; set; } = "00000";
}

public class CreateOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = "";
    public decimal Total { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
}

/// <summary>
/// Places an order from catalog items for the signed-in shopper. The order starts awaiting payment.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize] async (CreateOrderRequest request, IHttpContextAccessor httpContextAccessor, IPaymentService paymentService) =>
                await HandleAsync(request, httpContextAccessor, paymentService))
            .Produces<CreateOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CreateOrderRequest request) => throw new NotSupportedException();

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IHttpContextAccessor httpContextAccessor, IPaymentService paymentService)
    {
        var buyerId = httpContextAccessor.HttpContext.User.RequireBuyerId();

        var address = request.ShipToAddress == null
            ? new Address("123 Default Street", "Springfield", "ST", "US", "00000")
            : new Address(request.ShipToAddress.Street, request.ShipToAddress.City,
                request.ShipToAddress.State, request.ShipToAddress.Country, request.ShipToAddress.ZipCode);

        var order = await paymentService.PlaceOrderAsync(buyerId,
            request.Items.Select(i => (i.CatalogItemId, i.Quantity)).ToList(), address);

        var response = new CreateOrderResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total()
        };
        foreach (var item in order.OrderItems)
        {
            response.Items.Add(item.ToDto());
        }
        return Results.Ok(response);
    }
}

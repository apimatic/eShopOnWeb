using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, IOrderPaymentService paymentService, HttpContext httpContext) =>
            {
                return await HandleAsync(request, paymentService, httpContext);
            })
            .Produces<OrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PlaceOrderRequest request, IOrderPaymentService paymentService) =>
        HandleAsync(request, paymentService, null!);

    private async Task<IResult> HandleAsync(
        PlaceOrderRequest request,
        IOrderPaymentService paymentService,
        HttpContext httpContext)
    {
        var buyerId = httpContext.GetRequiredUserName();
        var items = new List<PlaceOrderItem>();
        foreach (var item in request.Items ?? new List<PlaceOrderItemRequest>())
        {
            items.Add(new PlaceOrderItem(item.CatalogItemId, item.Quantity));
        }

        Address? shipTo = null;
        if (request.ShipToAddress != null)
        {
            shipTo = new Address(
                request.ShipToAddress.Street ?? "123 Main Street",
                request.ShipToAddress.City ?? "Seattle",
                request.ShipToAddress.State ?? "WA",
                request.ShipToAddress.Country ?? "US",
                request.ShipToAddress.ZipCode ?? "98101");
        }

        var order = await paymentService.PlaceOrderAsync(buyerId, items, shipTo);
        var response = PaymentMapping.ToOrderResponse(order);
        return Results.Created($"api/orders/{order.Id}", response);
    }
}

public class PlaceOrderRequest
{
    public List<PlaceOrderItemRequest> Items { get; set; } = new();
    public ShipToAddressRequest? ShipToAddress { get; set; }
}

public class PlaceOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShipToAddressRequest
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

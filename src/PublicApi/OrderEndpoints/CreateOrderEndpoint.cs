using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, ICheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateOrderRequest request, ICheckoutService checkout, ClaimsPrincipal user) =>
            {
                return await HandleAsync(request.WithBuyer(CurrentBuyer.Id(user)), checkout);
            })
            .Produces<OrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, ICheckoutService checkout)
    {
        var lines = (request.Items ?? new List<CreateOrderItemRequest>())
            .ConvertAll(i => new OrderLine(i.CatalogItemId, i.Quantity));

        var order = await checkout.PlaceOrderAsync(
            request.BuyerId!,
            lines,
            PaymentRequestMapper.ToShippingAddress(request.ShippingAddress));

        return Results.Created($"api/orders/{order.Id}", PaymentRequestMapper.ToOrderResponse(order));
    }
}

public class CreateOrderRequest
{
    public List<CreateOrderItemRequest>? Items { get; set; }
    public ShippingAddressRequest? ShippingAddress { get; set; }
    internal string? BuyerId { get; private set; }

    internal CreateOrderRequest WithBuyer(string buyerId)
    {
        BuyerId = buyerId;
        return this;
    }
}

public class CreateOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

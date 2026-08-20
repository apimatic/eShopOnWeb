using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateOrderRequest request, ClaimsPrincipal user, IOrderPaymentService paymentService) =>
            {
                request.BuyerId = user.GetBuyerId();
                return await HandleAsync(request, paymentService);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderPaymentService paymentService)
    {
        if (request.Items is null || request.Items.Count == 0)
        {
            return Results.BadRequest(new { message = "An order must contain at least one item." });
        }

        var placed = await paymentService.PlaceOrderAsync(
            request.BuyerId,
            request.Items.Select(item => new OrderLineRequest(item.CatalogItemId, item.Quantity)).ToList(),
            PaymentMapping.ToAddress(request.ShippingAddress));

        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = placed.Order.Id,
            Order = PaymentDtoFactory.From(placed.Order, placed.Payment)
        };

        return Results.Created($"api/orders/{placed.Order.Id}", response);
    }
}

public class CreateOrderRequest : BaseRequest
{
    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;
    public List<CreateOrderItemRequest> Items { get; set; } = new();
    public ShippingAddressRequest? ShippingAddress { get; set; }
}

public class CreateOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId) { }

    public CreateOrderResponse() { }

    public int OrderId { get; set; }
    public OrderDto Order { get; set; } = new();
}

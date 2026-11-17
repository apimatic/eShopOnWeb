using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest : BaseRequest
{
    public List<CreateOrderItemRequest> Items { get; set; } = new();
    public ShippingAddressRequest? ShipToAddress { get; set; }
    public int? CatalogItemId { get; set; }
    public int? Quantity { get; set; }

    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId) {}
    public CreateOrderResponse() {}

    public int OrderId { get; set; }
    public OrderDto Order { get; set; } = new();
}

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, HttpContext httpContext, IPaymentService paymentService) =>
            {
                request.BuyerId = httpContext.User.Identity?.Name ?? string.Empty;
                return await HandleAsync(request, paymentService);
            })
            .Produces<CreateOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IPaymentService paymentService)
    {
        var response = new CreateOrderResponse(request.CorrelationId());

        var items = request.Items?.Where(item => item.CatalogItemId > 0).ToList() ?? new List<CreateOrderItemRequest>();
        if (items.Count == 0 && request.CatalogItemId.HasValue && request.Quantity.HasValue)
        {
            items.Add(new CreateOrderItemRequest
            {
                CatalogItemId = request.CatalogItemId.Value,
                Quantity = request.Quantity.Value
            });
        }

        var address = request.ShipToAddress is null
            ? new Address("1 eShop Way", "Redmond", "WA", "USA", "98052")
            : new Address(request.ShipToAddress.Street,
                request.ShipToAddress.City,
                request.ShipToAddress.State,
                request.ShipToAddress.Country,
                request.ShipToAddress.ZipCode);

        var order = await paymentService.CreateOrderAsync(request.BuyerId,
            items.Select(item => new CreateOrderItem
            {
                CatalogItemId = item.CatalogItemId,
                Quantity = item.Quantity
            }).ToList(),
            address);

        response.OrderId = order.Id;
        response.Order = OrderDtoMapper.ToDto(order);
        return Results.Created($"api/orders/{order.Id}", response);
    }
}

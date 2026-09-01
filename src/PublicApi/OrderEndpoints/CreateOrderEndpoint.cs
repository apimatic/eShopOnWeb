using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Place an order from catalog items. The order starts in a state awaiting payment.
/// </summary>
public class CreateOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user,
                IRepository<Order> orderRepository, IRepository<CatalogItem> catalogItemRepository) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                if (request.Items is null || request.Items.Count == 0 || request.Items.Any(i => i.Quantity <= 0))
                {
                    throw new PaymentException("An order requires at least one item with a positive quantity.");
                }

                var requestedIds = request.Items.Select(i => i.CatalogItemId).ToArray();
                var catalogItems = await catalogItemRepository.ListAsync(new CatalogItemsSpecification(requestedIds));
                if (catalogItems.Count != requestedIds.Distinct().Count())
                {
                    throw new NotFoundException("One or more catalog items were not found.");
                }

                var orderItems = request.Items
                    .Select(i =>
                    {
                        var catalogItem = catalogItems.First(c => c.Id == i.CatalogItemId);
                        return new OrderItem(
                            new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri),
                            catalogItem.Price, i.Quantity);
                    })
                    .ToList();

                var shipTo = request.ShipTo?.ToAddress()
                    ?? new Address("123 Main St.", "Kent", "OH", "United States", "44240");

                var order = new Order(buyerId, shipTo, orderItems);
                order = await orderRepository.AddAsync(order);

                var response = new CreateOrderResponse(request.CorrelationId())
                {
                    OrderId = order.Id,
                    Total = order.Total(),
                    PaymentStatus = "AwaitingPayment"
                };
                return Results.Created($"api/orders/{order.Id}", response);
            })
            .Produces<CreateOrderResponse>()
            .WithTags("OrderEndpoints");
    }
}

public class CreateOrderRequest : BaseRequest
{
    public List<OrderItemRequest> Items { get; set; } = new List<OrderItemRequest>();
    public AddressRequest? ShipTo { get; set; }
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId) { }

    public int OrderId { get; set; }
    public decimal Total { get; set; }
    public string PaymentStatus { get; set; } = "AwaitingPayment";
}

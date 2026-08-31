using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
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
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest : BaseRequest
{
    [Required]
    [MinLength(1)]
    public List<CreateOrderItem> Items { get; set; } = new List<CreateOrderItem>();

    public ShippingAddressDto? ShipToAddress { get; set; }
}

public class CreateOrderItem
{
    [Required]
    public int CatalogItemId { get; set; }

    [Range(1, 10000)]
    public int Quantity { get; set; } = 1;
}

public class ShippingAddressDto
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class CreateOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
}

/// <summary>
/// Places an order from catalog items. The order starts in PendingPayment state,
/// awaiting payment via POST /api/orders/{orderId}/pay.
/// </summary>
public class CreateOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request,
             ClaimsPrincipal user,
             IRepository<Order> orderRepository,
             IRepository<CatalogItem> itemRepository,
             IRepository<Payment> paymentRepository,
             IOptions<PayPalSettings> payPalSettings) =>
            {
                return await HandleAsync(request, user, orderRepository, itemRepository, paymentRepository, payPalSettings);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, ClaimsPrincipal user,
        IRepository<Order> orderRepository, IRepository<CatalogItem> itemRepository,
        IRepository<Payment> paymentRepository, IOptions<PayPalSettings> payPalSettings)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        if (request.Items.Count == 0)
        {
            return Results.BadRequest(new { message = "At least one item is required." });
        }

        var catalogItemsSpec = new CatalogItemsSpecification(request.Items.Select(i => i.CatalogItemId).ToArray());
        var catalogItems = await itemRepository.ListAsync(catalogItemsSpec);

        var missing = request.Items.Select(i => i.CatalogItemId).Except(catalogItems.Select(c => c.Id)).ToList();
        if (missing.Count > 0)
        {
            return Results.BadRequest(new { message = $"Unknown catalog item ids: {string.Join(", ", missing)}" });
        }

        var orderItems = request.Items.Select(i =>
        {
            var catalogItem = catalogItems.First(c => c.Id == i.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri ?? "eCatalog-item-default.png");
            return new OrderItem(itemOrdered, catalogItem.Price, i.Quantity);
        }).ToList();

        var address = new Address(
            request.ShipToAddress?.Street ?? "N/A",
            request.ShipToAddress?.City ?? "N/A",
            request.ShipToAddress?.State ?? "N/A",
            request.ShipToAddress?.Country ?? "N/A",
            request.ShipToAddress?.ZipCode ?? "N/A");

        var order = new Order(buyerId, address, orderItems);
        order = await orderRepository.AddAsync(order);

        var payment = new Payment(order.Id, buyerId, order.Total(), payPalSettings.Value.Currency);
        await paymentRepository.AddAsync(payment);

        var response = new CreateOrderResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total(),
            Currency = payment.Currency
        };

        return Results.Created($"api/orders/{order.Id}", response);
    }
}

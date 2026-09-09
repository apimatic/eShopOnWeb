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
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.Payments;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderPaymentEndpoints;

public record PlaceOrderItem(int CatalogItemId, int Quantity);

public record PlaceOrderRequest(List<PlaceOrderItem>? Items, ShippingAddressInput? ShipToAddress);

public record ShippingAddressInput(string? Street, string? City, string? State, string? Country, string? ZipCode);

public class PlaceOrderContext
{
    public string BuyerId { get; init; } = string.Empty;
    public PlaceOrderRequest Request { get; init; } = new(null, null);
}

/// <summary>
/// Places an order from catalog items for the signed-in shopper and creates its payment record in the
/// awaiting-payment state. Reuses the app's existing Order/OrderItem model.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderContext>
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IUriComposer _uriComposer;
    private readonly PayPalOptions _options;

    public PlaceOrderEndpoint(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<OrderPayment> paymentRepository,
        IUriComposer uriComposer,
        IOptions<PayPalOptions> options)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentRepository = paymentRepository;
        _uriComposer = uriComposer;
        _options = options.Value;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, ClaimsPrincipal user) =>
                await HandleAsync(new PlaceOrderContext { BuyerId = user.GetBuyerId(), Request = request }))
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderContext context)
    {
        var request = context.Request;
        if (request.Items is null || request.Items.Count == 0)
            return Results.BadRequest(new { message = "At least one order item is required." });

        if (request.Items.Any(i => i.Quantity <= 0))
            return Results.BadRequest(new { message = "Item quantities must be greater than zero." });

        var itemIds = request.Items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(itemIds));
        if (catalogItems.Count != itemIds.Length)
            return Results.BadRequest(new { message = "One or more catalog items could not be found." });

        var orderItems = request.Items.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var address = ToAddress(request.ShipToAddress);
        var order = new Order(context.BuyerId, address, orderItems);
        await _orderRepository.AddAsync(order);

        var payment = new OrderPayment(order.Id, context.BuyerId, _options.Currency, order.Total());
        await _paymentRepository.AddAsync(payment);

        var response = new PlaceOrderResponse
        {
            OrderId = order.Id,
            Status = payment.Status.ToString(),
            Currency = payment.Currency,
            Amount = payment.Amount
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }

    private static Address ToAddress(ShippingAddressInput? input) => new(
        string.IsNullOrWhiteSpace(input?.Street) ? "N/A" : input!.Street,
        string.IsNullOrWhiteSpace(input?.City) ? "N/A" : input!.City,
        input?.State ?? "N/A",
        string.IsNullOrWhiteSpace(input?.Country) ? "N/A" : input!.Country,
        string.IsNullOrWhiteSpace(input?.ZipCode) ? "00000" : input!.ZipCode);
}

public class PlaceOrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

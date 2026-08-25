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
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using Microsoft.eShopWeb.PublicApi.PaymentShared;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PlaceOrderRequestItem
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class PlaceOrderRequestBody
{
    public AddressRequestDto ShipToAddress { get; set; } = new();
    public List<PlaceOrderRequestItem> Items { get; set; } = new();
}

public class PlaceOrderRequest : BaseRequest
{
    public PlaceOrderRequest(PlaceOrderRequestBody body, string buyerId)
    {
        Body = body;
        BuyerId = buyerId;
    }

    public PlaceOrderRequestBody Body { get; }
    public string BuyerId { get; }
}

public class PlaceOrderResponse : BaseResponse
{
    public PlaceOrderResponse(Guid correlationId) : base(correlationId) { }

    public int OrderId { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Places an order for the signed-in shopper from catalog item ids/quantities. The order
/// starts AwaitingPayment; call POST api/orders/{orderId}/pay next to authorize payment.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, PaymentDependencies>
{
    private readonly IUriComposer _uriComposer;

    public PlaceOrderEndpoint(IUriComposer uriComposer)
    {
        _uriComposer = uriComposer;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequestBody body, ClaimsPrincipal user,
             IRepository<Order> orderRepository, IRepository<Payment> paymentRepository, IRepository<Buyer> buyerRepository,
             IRepository<CatalogItem> catalogItemRepository, IPayPalClient payPalClient, IOptions<PayPalOptions> payPalOptions) =>
            {
                var request = new PlaceOrderRequest(body, user.Identity!.Name!);
                var deps = new PaymentDependencies(orderRepository, paymentRepository, buyerRepository, catalogItemRepository, payPalClient, payPalOptions.Value);
                return await HandleAsync(request, deps);
            })
            .Produces<PlaceOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, PaymentDependencies deps)
    {
        var response = new PlaceOrderResponse(request.CorrelationId());

        if (request.Body.Items == null || request.Body.Items.Count == 0)
        {
            return Results.BadRequest("An order must contain at least one item.");
        }

        var catalogItemIds = request.Body.Items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await deps.CatalogItemRepository.ListAsync(new CatalogItemsSpecification(catalogItemIds));

        var orderItems = new List<OrderItem>();
        foreach (var line in request.Body.Items)
        {
            if (line.Quantity <= 0)
            {
                return Results.BadRequest($"Quantity for catalog item {line.CatalogItemId} must be greater than zero.");
            }

            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId);
            if (catalogItem == null)
            {
                return Results.BadRequest($"Catalog item {line.CatalogItemId} was not found.");
            }

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var shipTo = request.Body.ShipToAddress;
        var address = new Address(shipTo.Street, shipTo.City, shipTo.State, shipTo.Country, shipTo.ZipCode);

        var order = new Order(request.BuyerId, address, orderItems);
        order = await deps.OrderRepository.AddAsync(order);

        var payment = new Payment(order.Id, order.Total(), deps.PayPalOptions.Currency);
        await deps.PaymentRepository.AddAsync(payment);

        response.OrderId = order.Id;
        response.Total = order.Total();
        response.Currency = deps.PayPalOptions.Currency;
        response.Status = order.Status.ToString();

        return Results.Created($"api/orders/{order.Id}", response);
    }
}

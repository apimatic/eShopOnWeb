using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
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
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest
{
    public List<OrderItemRequest> Items { get; set; } = new();
    public ShippingAddressRequest? ShippingAddress { get; set; }
}

public class OrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShippingAddressRequest
{
    public string Street { get; set; } = "";
    public string City { get; set; } = "";
    public string State { get; set; } = "";
    public string Country { get; set; } = "";
    public string ZipCode { get; set; } = "";
}

public class CreateOrderResponse
{
    public int OrderId { get; set; }
    public decimal Total { get; set; }
    public string PaymentStatus { get; set; } = "";
    public string Currency { get; set; } = "";
}

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IRepository<CatalogItem>>
{
    private readonly IRepository<Order> _orderRepo;
    private readonly IRepository<OrderPayment> _paymentRepo;
    private readonly IOptions<PayPalSettings> _paypalSettings;

    public CreateOrderEndpoint(
        IRepository<Order> orderRepo,
        IRepository<OrderPayment> paymentRepo,
        IOptions<PayPalSettings> paypalSettings)
    {
        _orderRepo = orderRepo;
        _paymentRepo = paymentRepo;
        _paypalSettings = paypalSettings;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateOrderRequest request, IRepository<CatalogItem> catalogRepo,
                   HttpContext ctx, CancellationToken ct) =>
            {
                var buyerId = ctx.User.FindFirst(ClaimTypes.Name)?.Value;
                return await HandleAsync(request, catalogRepo, buyerId, ct);
            })
            .Produces<CreateOrderResponse>(201)
            .Produces(400)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CreateOrderRequest request, IRepository<CatalogItem> repository)
        => HandleAsync(request, repository, null);

    private async Task<IResult> HandleAsync(CreateOrderRequest request,
        IRepository<CatalogItem> catalogRepo, string? buyerId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        if (request.Items == null || !request.Items.Any())
            return Results.BadRequest(new { error = "At least one item is required." });

        var itemIds = request.Items.Select(i => i.CatalogItemId).Distinct().ToList();
        var catalogItems = new List<CatalogItem?>();
        foreach (var id in itemIds)
            catalogItems.Add(await catalogRepo.GetByIdAsync(id, ct));

        var missing = itemIds.Where((id, idx) => catalogItems[idx] == null).ToList();
        if (missing.Any())
            return Results.BadRequest(new { error = $"Catalog items not found: {string.Join(", ", missing)}" });

        var catalogMap = catalogItems
            .Where(c => c != null)
            .ToDictionary(c => c!.Id, c => c!);

        var orderItems = request.Items.Select(item =>
        {
            var ci = catalogMap[item.CatalogItemId];
            return new OrderItem(
                new CatalogItemOrdered(ci.Id, ci.Name, ci.PictureUri),
                ci.Price,
                item.Quantity);
        }).ToList();

        var addr = request.ShippingAddress;
        var shipTo = addr != null
            ? new Address(addr.Street, addr.City, addr.State, addr.Country, addr.ZipCode)
            : new Address("TBD", "TBD", "TBD", "US", "00000");

        var order = new Order(buyerId, shipTo, orderItems);
        await _orderRepo.AddAsync(order, ct);

        var currency = _paypalSettings.Value.Currency;
        var payment = new OrderPayment(order.Id, buyerId, order.Total(), currency);
        await _paymentRepo.AddAsync(payment, ct);

        return Results.Created($"/api/orders/{order.Id}", new CreateOrderResponse
        {
            OrderId = order.Id,
            Total = order.Total(),
            PaymentStatus = payment.Status.ToString(),
            Currency = currency
        });
    }
}

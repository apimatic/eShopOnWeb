using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Finix;
using MinimalApi.Endpoint;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest
{
    public List<CreateOrderItemRequest> Items { get; set; } = new();
    public ShippingAddressRequest? ShippingAddress { get; set; }
    public ShippingAddressRequest? Address { get; set; }
}

public class CreateOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int? Id { get; set; }
    public int Quantity { get; set; } = 1;
    public int? Qty { get; set; }
}

public class ShippingAddressRequest
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId) { }

    public int OrderId { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
}

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest>
{
    private readonly CatalogContext _db;
    private readonly IUriComposer _uriComposer;
    private readonly IOptions<FinixOptions> _finixOptions;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateOrderEndpoint(CatalogContext db, IUriComposer uriComposer, IOptions<FinixOptions> finixOptions,
        IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _uriComposer = uriComposer;
        _finixOptions = finixOptions;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request) => await HandleAsync(request))
            .Produces<CreateOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request)
    {
        var ownerId = PaymentHelpers.OwnerId(_httpContextAccessor.HttpContext?.User);
        if (string.IsNullOrWhiteSpace(ownerId))
        {
            return Results.Unauthorized();
        }

        var requestedItems = (request.Items ?? new List<CreateOrderItemRequest>())
            .Select(i => new CreateOrderItemRequest
            {
                CatalogItemId = i.CatalogItemId != 0 ? i.CatalogItemId : (i.Id ?? 0),
                Quantity = i.Qty ?? i.Quantity
            })
            .Where(i => i.Quantity > 0)
            .GroupBy(i => i.CatalogItemId)
            .Select(g => new CreateOrderItemRequest
            {
                CatalogItemId = g.Key,
                Quantity = g.Sum(i => i.Quantity)
            })
            .ToList();

        if (requestedItems.Count == 0)
        {
            return Results.BadRequest(new { message = "At least one catalog item with a positive quantity is required." });
        }

        var catalogItemIds = requestedItems.Select(i => i.CatalogItemId).ToArray();
        var catalogItems = await _db.CatalogItems
            .Where(i => catalogItemIds.Contains(i.Id))
            .ToListAsync();

        if (catalogItems.Count != catalogItemIds.Length)
        {
            var missing = catalogItemIds.Except(catalogItems.Select(i => i.Id)).ToArray();
            return Results.BadRequest(new { message = $"Unknown catalog item ids: {string.Join(", ", missing)}." });
        }

        var orderItems = requestedItems.Select(requested =>
        {
            var catalogItem = catalogItems.First(i => i.Id == requested.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, requested.Quantity);
        }).ToList();

        var address = BuildAddress(request.ShippingAddress ?? request.Address);
        var order = new Order(ownerId, address, orderItems);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        var response = new CreateOrderResponse(Guid.NewGuid())
        {
            OrderId = order.Id,
            Total = order.Total(),
            Currency = _finixOptions.Value.Currency,
            Status = order.Status.ToString(),
            OrderDate = order.OrderDate
        };

        return Results.Created($"api/orders/{order.Id}", response);
    }

    private static Address BuildAddress(ShippingAddressRequest? request)
    {
        return new Address(
            request?.Street ?? "1 Main Street",
            request?.City ?? "Seattle",
            request?.State ?? "WA",
            request?.Country ?? "USA",
            request?.ZipCode ?? "98101");
    }
}

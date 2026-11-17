using System;
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
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;
using OrderAddress = Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.Address;
using OrderItemEntity = Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.OrderItem;
using OrderEntity = Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.Order;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, ClaimsPrincipal>
{
    private readonly CatalogContext _db;
    private readonly FinixPaymentService _payments;

    public CreateOrderEndpoint(CatalogContext db, FinixPaymentService payments)
    {
        _db = db;
        _payments = payments;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (CreateOrderRequest request, ClaimsPrincipal user) => await HandleAsync(request, user))
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, ClaimsPrincipal user)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            return PaymentResults.Unauthorized();
        }

        var lines = request.EffectiveItems;
        if (lines.Count == 0)
        {
            return Results.BadRequest(new { message = "At least one catalog item is required." });
        }

        if (lines.Any(line => line.EffectiveQuantity <= 0))
        {
            return Results.BadRequest(new { message = "Every item quantity must be greater than zero." });
        }

        var itemIds = lines.Select(line => line.EffectiveCatalogItemId).Distinct().ToList();
        var catalogItems = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .ToListAsync(_db.CatalogItems.Where(item => itemIds.Contains(item.Id)));

        var catalogById = catalogItems.ToDictionary(item => item.Id);
        var missing = itemIds.Where(id => !catalogById.ContainsKey(id)).ToList();
        if (missing.Count > 0)
        {
            return Results.BadRequest(new
            {
                message = $"Unknown catalog item ids: {string.Join(", ", missing)}."
            });
        }

        var orderItems = new List<OrderItemEntity>();
        foreach (var line in lines)
        {
            var catalogItem = catalogById[line.EffectiveCatalogItemId];
            var pictureUri = string.IsNullOrWhiteSpace(catalogItem.PictureUri)
                ? "eShop-item-default.png"
                : catalogItem.PictureUri;
            var ordered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            orderItems.Add(new OrderItemEntity(ordered, catalogItem.Price, line.EffectiveQuantity));
        }

        var addressRequest = request.EffectiveAddress;
        var address = addressRequest is null
            ? new OrderAddress("1 Main Street", "San Francisco", "CA", "USA", "94105")
            : new OrderAddress(
                addressRequest.EffectiveStreet,
                addressRequest.EffectiveCity,
                addressRequest.EffectiveState,
                addressRequest.EffectiveCountry,
                addressRequest.EffectiveZip);

        var order = new OrderEntity(buyerId, address, orderItems);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate.OrderPayment payment;
        try
        {
            payment = await _payments.CreatePaymentAsync(order, CancellationToken.None);
        }
        catch (PaymentOperationException exception)
        {
            return PaymentResults.FromException(exception);
        }

        var response = new CreateOrderResponse
        {
            OrderId = order.Id,
            Order = PaymentResponseMapper.ToOrderResponse(order, payment, [])
        };

        return Results.Created($"api/orders/{order.Id}", response);
    }
}

using System;
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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order from catalog items at current catalog prices. The order starts awaiting payment.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IOrderPaymentService orderService, CancellationToken ct) =>
            {
                request.BuyerId = user.Identity?.Name ?? string.Empty;
                return await HandleAsync(request, orderService, ct);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CreateOrderRequest request, IOrderPaymentService orderService)
    {
        return HandleAsync(request, orderService, CancellationToken.None);
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderPaymentService orderService, CancellationToken ct)
    {
        try
        {
            Address? address = request.ShipToAddress is null
                ? null
                : new Address(request.ShipToAddress.Street, request.ShipToAddress.City,
                    request.ShipToAddress.State, request.ShipToAddress.Country, request.ShipToAddress.ZipCode);

            var order = await orderService.CreateOrderAsync(request.BuyerId,
                request.Items.Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity)).ToList(),
                address, ct);

            var response = new CreateOrderResponse(request.CorrelationId())
            {
                OrderId = order.Id,
                PaymentStatus = order.PaymentStatus.ToString(),
                Total = order.Total(),
                Currency = order.Currency,
                Items = OrderDto.FromOrder(order).Items
            };
            return Results.Created($"api/orders/{order.Id}", response);
        }
        catch (Exception ex) when (EndpointErrorMapper.TryMap(ex, out var error))
        {
            return error;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, ICheckoutPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateOrderRequest request, ICheckoutPaymentService payments, HttpContext http) =>
            {
                var order = await payments.CreateOrderAsync(
                    http.User.GetBuyerId(),
                    request.Items.Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity)).ToList(),
                    ToAddress(request.ShipTo));
                var dto = OrderResponseMapper.ToDto(order);
                return Results.Created($"api/orders/{dto.OrderId}", new CreateOrderResponse { OrderId = dto.OrderId, Order = dto });
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CreateOrderRequest request, ICheckoutPaymentService payments) =>
        Task.FromResult(Results.StatusCode(StatusCodes.Status501NotImplemented));

    private static Address? ToAddress(AddressDto? shipTo)
    {
        if (shipTo == null || string.IsNullOrWhiteSpace(shipTo.Street))
        {
            return null;
        }

        return new Address(shipTo.Street, shipTo.City, shipTo.State, shipTo.Country, shipTo.ZipCode);
    }
}

public class CreateOrderRequest
{
    public List<CreateOrderItemRequest> Items { get; set; } = new();
    public AddressDto? ShipTo { get; set; }
}

public class CreateOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderResponse
{
    public int OrderId { get; set; }
    public OrderDto Order { get; set; } = new();
}

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, ICheckoutPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, PayOrderRequest request, ICheckoutPaymentService payments, HttpContext http) =>
            {
                var order = await payments.AuthorizePaymentAsync(
                    orderId,
                    http.User.GetBuyerId(),
                    CardRequestMapper.ToCardDetails(request.Card),
                    request.PaymentMethodId);
                return Results.Ok(OrderResponseMapper.ToDto(order));
            })
            .Produces<OrderDto>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PayOrderRequest request, ICheckoutPaymentService payments) =>
        Task.FromResult(Results.StatusCode(StatusCodes.Status501NotImplemented));
}

public class PayOrderRequest
{
    public CardRequestDto? Card { get; set; }
    public int? PaymentMethodId { get; set; }
}

public class FulfilOrderEndpoint : IEndpoint<IResult, int, ICheckoutPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, ICheckoutPaymentService payments) =>
            {
                var order = await payments.FulfilOrderAsync(orderId);
                return Results.Ok(OrderResponseMapper.ToDto(order));
            })
            .Produces<OrderDto>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(int request, ICheckoutPaymentService payments) =>
        Task.FromResult(Results.StatusCode(StatusCodes.Status501NotImplemented));
}

public class CancelOrderEndpoint : IEndpoint<IResult, int, ICheckoutPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, ICheckoutPaymentService payments) =>
            {
                var order = await payments.CancelOrderAsync(orderId);
                return Results.Ok(OrderResponseMapper.ToDto(order));
            })
            .Produces<OrderDto>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(int request, ICheckoutPaymentService payments) =>
        Task.FromResult(Results.StatusCode(StatusCodes.Status501NotImplemented));
}

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, ICheckoutPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, RefundOrderRequest request, ICheckoutPaymentService payments, HttpContext http) =>
            {
                var refund = await payments.RefundOrderAsync(
                    orderId,
                    http.User.GetBuyerId(),
                    http.User.IsAdministrator(),
                    request.IdempotencyKey,
                    request.Amount);
                var dto = OrderResponseMapper.ToDto(refund);
                return Results.Ok(new RefundOrderResponse
                {
                    RefundId = dto.RefundId,
                    Refund = dto
                });
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(RefundOrderRequest request, ICheckoutPaymentService payments) =>
        Task.FromResult(Results.StatusCode(StatusCodes.Status501NotImplemented));
}

public class RefundOrderRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
}

public class RefundOrderResponse
{
    public int RefundId { get; set; }
    public RefundDto Refund { get; set; } = new();
}

public class ListMyOrdersEndpoint : IEndpoint<IResult, ICheckoutPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ICheckoutPaymentService payments, HttpContext http) =>
            {
                var orders = await payments.ListBuyerOrdersAsync(http.User.GetBuyerId());
                return Results.Ok(new ListMyOrdersResponse
                {
                    Orders = orders.Select(OrderResponseMapper.ToDto).ToList()
                });
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(ICheckoutPaymentService payments) =>
        Task.FromResult(Results.StatusCode(StatusCodes.Status501NotImplemented));
}

public class ListMyOrdersResponse
{
    public List<OrderDto> Orders { get; set; } = new();
}

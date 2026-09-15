using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Operator action: fulfil an order, capturing the held funds. That is when the money actually moves.
/// A stale authorization is renewed first where possible. Administrator role only.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, int>
{
    private readonly IPaymentService _paymentService;
    private readonly IRepository<Order> _orderRepository;

    public FulfilOrderEndpoint(IPaymentService paymentService, IRepository<Order> orderRepository)
    {
        _paymentService = paymentService;
        _orderRepository = orderRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId) => await HandleAsync(orderId))
            .Produces<FulfilOrderResponse>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId)
    {
        var response = new FulfilOrderResponse();

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId));
        if (order is null)
        {
            throw new EntityNotFoundException($"Order {orderId} was not found.");
        }

        await _paymentService.FulfilOrderAsync(order);

        response.OrderId = order.Id;
        response.Order = OrderDto.From(order);
        return Results.Ok(response);
    }
}

public class FulfilOrderResponse : BaseResponse
{
    public FulfilOrderResponse(Guid correlationId) : base(correlationId) { }
    public FulfilOrderResponse() { }

    public int OrderId { get; set; }
    public OrderDto? Order { get; set; }
}

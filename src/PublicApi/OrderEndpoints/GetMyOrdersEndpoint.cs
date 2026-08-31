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
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Lists the caller's own orders together with their payment state.
/// </summary>
public class GetMyOrdersEndpoint : IEndpoint<IResult, GetMyOrdersRequest, ClaimsPrincipal>
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly PayPalSettings _payPalSettings;

    public GetMyOrdersEndpoint(IRepository<Order> orderRepository,
        IRepository<OrderPayment> paymentRepository,
        IOptions<PayPalSettings> payPalSettings)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _payPalSettings = payPalSettings.Value;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user) =>
            {
                return await HandleAsync(new GetMyOrdersRequest(), user);
            })
            .Produces<GetMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(GetMyOrdersRequest request, ClaimsPrincipal user)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId));
        var payments = await _paymentRepository.ListAsync(new OrderPaymentsByBuyerSpec(buyerId));
        var paymentsByOrder = payments.ToDictionary(p => p.OrderId);

        var response = new GetMyOrdersResponse(request.CorrelationId())
        {
            Orders = orders
                .OrderByDescending(o => o.OrderDate)
                .Select(o => OrderDtoMapper.Map(o,
                    paymentsByOrder.TryGetValue(o.Id, out var payment) ? payment : null,
                    _payPalSettings.Currency))
                .ToList()
        };
        return Results.Ok(response);
    }
}

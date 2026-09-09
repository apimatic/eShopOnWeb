using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderPaymentEndpoints;

/// <summary>Lists the signed-in shopper's orders with their payment state.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, string>
{
    private readonly IRepository<OrderPayment> _paymentRepository;

    public MyOrdersEndpoint(IRepository<OrderPayment> paymentRepository)
    {
        _paymentRepository = paymentRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user) => await HandleAsync(user.GetBuyerId()))
            .Produces<List<OrderPaymentDto>>()
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(string buyerId)
    {
        var payments = await _paymentRepository.ListAsync(new OrderPaymentsByBuyerSpecification(buyerId));
        return Results.Ok(payments.Select(OrderPaymentDto.From).ToList());
    }
}

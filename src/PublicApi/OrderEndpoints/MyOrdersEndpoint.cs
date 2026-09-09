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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Lists the signed-in shopper's own orders, each with its payment state.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersRequest, IReadRepository<Order>>
{
    private readonly PayPalSettings _settings;

    public MyOrdersEndpoint(PayPalSettings settings) => _settings = settings;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, CancellationToken ct, IReadRepository<Order> repository) =>
            {
                return await HandleAsync(new MyOrdersRequest(user.GetUserId(), ct), repository);
            })
            .Produces<List<OrderPaymentDto>>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(MyOrdersRequest request, IReadRepository<Order> repository)
    {
        var orders = await repository.ListAsync(
            new CustomerOrdersWithPaymentSpecification(request.UserId), request.Cancellation);
        var dtos = orders.Select(o => OrderPaymentDto.From(o, _settings.Currency)).ToList();
        return Results.Ok(dtos);
    }
}

public record MyOrdersRequest(string UserId, CancellationToken Cancellation);

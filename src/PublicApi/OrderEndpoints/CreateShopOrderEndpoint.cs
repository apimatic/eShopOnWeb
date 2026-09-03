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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateShopOrderEndpoint : IEndpoint<IResult, CreateShopOrderRequest, ICheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateShopOrderRequest request, ClaimsPrincipal user, ICheckoutService checkout, CancellationToken ct) =>
            {
                return await HandleAsync(request, checkout, user, ct);
            })
            .Produces<CreateShopOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CreateShopOrderRequest request, ICheckoutService checkout) =>
        HandleAsync(request, checkout, new ClaimsPrincipal(), CancellationToken.None);

    private async Task<IResult> HandleAsync(
        CreateShopOrderRequest request,
        ICheckoutService checkout,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var ship = request.ShipTo ?? new ShippingAddressRequest();
        var result = await checkout.PlaceOrderAsync(
            CallerIdentity.BuyerId(user),
            (request.Items ?? new List<CreateShopOrderItem>())
                .Select(i => new PlaceOrderItem(i.CatalogItemId, i.Quantity))
                .ToList(),
            new Address(ship.Street, ship.City, ship.State, ship.Country, ship.ZipCode),
            ct);

        var response = new CreateShopOrderResponse
        {
            OrderId = result.OrderId,
            Total = result.Total,
            Currency = result.Currency,
            Status = result.Status.ToString()
        };
        return Results.Created($"api/orders/{result.OrderId}", response);
    }
}

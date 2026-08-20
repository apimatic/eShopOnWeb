using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, ICheckoutService>
{
    private readonly IPaymentSettings _paymentSettings;

    public CreateOrderEndpoint(IPaymentSettings paymentSettings)
    {
        _paymentSettings = paymentSettings;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateOrderRequest request, HttpContext http, ICheckoutService checkout) =>
            {
                request.BuyerId = RequireBuyerId(http);
                return await HandleAsync(request, checkout);
            })
            .Produces<OrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, ICheckoutService checkout)
    {
        var shipTo = request.ShipTo is null
            ? null
            : new Address(request.ShipTo.Street, request.ShipTo.City, request.ShipTo.State, request.ShipTo.Country, request.ShipTo.ZipCode);

        var items = (request.Items ?? []).Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity)).ToList();
        var order = await checkout.PlaceOrderAsync(request.BuyerId!, items, shipTo);
        var response = OrderResponse.From(order, _paymentSettings.Currency);
        return Results.Created($"api/orders/{response.OrderId}", response);
    }

    internal static string RequireBuyerId(HttpContext http)
    {
        var name = http.User.Identity?.Name ?? http.User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new PaymentException("The caller is not authenticated.", 401);
        }

        return name;
    }
}

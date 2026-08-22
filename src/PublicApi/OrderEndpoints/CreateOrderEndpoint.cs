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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, ClaimsPrincipal>
{
    private readonly IShopperCheckoutService _checkoutService;
    private readonly IPaymentCurrencyAccessor _currency;

    public CreateOrderEndpoint(IShopperCheckoutService checkoutService, IPaymentCurrencyAccessor currency)
    {
        _checkoutService = checkoutService;
        _currency = currency;
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
        var buyerId = user.RequireUserName();
        var items = (request.Items ?? new()).Select(i => (i.CatalogItemId, i.Quantity)).ToList();
        var address = ToAddress(request.ShipToAddress);
        var order = await _checkoutService.PlaceOrderAsync(buyerId, items, address);
        var response = new CreateOrderResponse
        {
            OrderId = order.Id,
            Order = OrderDtoMapper.FromOrder(order, _currency.Currency)
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }

    private static Address ToAddress(CreateOrderAddressRequest? request)
    {
        if (request is null
            || string.IsNullOrWhiteSpace(request.Street)
            || string.IsNullOrWhiteSpace(request.City)
            || string.IsNullOrWhiteSpace(request.Country)
            || string.IsNullOrWhiteSpace(request.ZipCode))
        {
            return new Address("123 Main St.", "Kent", "OH", "United States", "44240");
        }

        return new Address(request.Street, request.City, request.State, request.Country, request.ZipCode);
    }
}

internal static class EndpointUserExtensions
{
    public static string RequireUserName(this ClaimsPrincipal user)
    {
        var name = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new PaymentException(401, "The caller is not authenticated.");
        }

        return name;
    }
}

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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order for the signed-in shopper from catalog items; it starts awaiting payment.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, OrderPlacementService>
{
    private readonly string _currency;

    public CreateOrderEndpoint(IOptions<PayPalOptions> payPalOptions)
    {
        _currency = payPalOptions.Value.Currency?.Trim().ToUpperInvariant() ?? string.Empty;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, OrderPlacementService service) =>
            {
                request.BuyerId = user.BuyerId();
                return await HandleAsync(request, service);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, OrderPlacementService service)
    {
        if (request.BuyerId is null)
            return Results.Unauthorized();

        var address = request.ShipToAddress;
        if (address is null || string.IsNullOrWhiteSpace(address.Street) || string.IsNullOrWhiteSpace(address.City) ||
            string.IsNullOrWhiteSpace(address.Country) || string.IsNullOrWhiteSpace(address.ZipCode))
            throw new PaymentValidationException("shipToAddress with street, city, country and zipCode is required.");

        var lines = (request.Items ?? new()).Select(i => new OrderLine(i.CatalogItemId, i.Quantity)).ToList();
        var order = await service.PlaceOrderAsync(
            request.BuyerId,
            lines,
            new Address(address.Street!, address.City!, address.State ?? string.Empty, address.Country!, address.ZipCode!),
            CancellationToken.None);

        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Order = OrderDtoMapper.ToDto(order, null, _currency),
        };
        return Results.Created($"api/my-orders", response);
    }
}

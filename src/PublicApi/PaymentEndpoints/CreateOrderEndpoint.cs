using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// POST /api/orders — place an order (awaiting payment) from catalog items for the signed-in shopper.
/// </summary>
public class CreateOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateOrderRequest request, IOrderPaymentService service, ClaimsPrincipal user,
                ApplicationCore.PaymentGateway.PayPalSettings settings) =>
            {
                var identity = CallerIdentity.Require(user);

                var lines = (request.Items ?? new())
                    .Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity))
                    .ToList();

                var address = ToAddress(request.ShipToAddress);
                var order = await service.PlaceOrderAsync(identity, lines, address);

                var response = new CreateOrderResponse(
                    order.Id,
                    order.Status.ToString(),
                    order.Total(),
                    settings.Currency,
                    order.OrderItems.Select(i => i.ToDto()).ToList());

                return Results.Created($"api/orders/{order.Id}", response);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderPaymentEndpoints");
    }

    private static Address ToAddress(ShipToAddressInput? input)
    {
        if (input is null)
        {
            return new Address("Not provided", "Not provided", string.Empty, "Not provided", "00000");
        }
        return new Address(input.Street, input.City, input.State, input.Country, input.ZipCode);
    }
}

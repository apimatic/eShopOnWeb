using System.Linq;
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

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, IOrderPaymentService service, HttpContext httpContext) =>
            {
                request.BuyerId = RequireBuyerId(httpContext);
                return await HandleAsync(request, service);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderPaymentService service)
    {
        var lines = request.Items.Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity)).ToList();
        var order = await service.PlaceOrderAsync(request.BuyerId!, lines, ToAddress(request.ShipTo), default);

        var response = new CreateOrderResponse
        {
            OrderId = order.Id,
            Order = OrderDtoMapper.Map(order)
        };

        return Results.Created($"api/orders/{order.Id}", response);
    }

    internal static string RequireBuyerId(HttpContext httpContext)
    {
        return httpContext.User.Identity?.Name
            ?? throw new CheckoutException(401, "The caller is not authenticated.");
    }

    internal static Address ToAddress(CreateOrderAddressRequest? shipTo)
    {
        if (shipTo == null || string.IsNullOrWhiteSpace(shipTo.Street))
        {
            return new Address("123 Main St.", "Kent", "OH", "United States", "44240");
        }

        return new Address(shipTo.Street, shipTo.City, shipTo.State, shipTo.Country, shipTo.ZipCode);
    }
}

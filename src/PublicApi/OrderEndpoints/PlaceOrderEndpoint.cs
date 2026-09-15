using System.Linq;
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

public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IOrderPaymentService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IPayPalPaymentsClient _payPal;

    public PlaceOrderEndpoint(IHttpContextAccessor httpContextAccessor, IPayPalPaymentsClient payPal)
    {
        _httpContextAccessor = httpContextAccessor;
        _payPal = payPal;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (PlaceOrderRequest request, IOrderPaymentService orders) => await HandleAsync(request, orders))
            .Produces<OrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, IOrderPaymentService orders)
    {
        var buyerId = CallerIdentity.GetBuyerId(_httpContextAccessor.HttpContext);
        Address? address = null;
        if (request.ShipToAddress is not null)
        {
            address = new Address(
                request.ShipToAddress.Street,
                request.ShipToAddress.City,
                request.ShipToAddress.State,
                request.ShipToAddress.Country,
                request.ShipToAddress.ZipCode);
        }

        var lines = (request.Items ?? Enumerable.Empty<PlaceOrderItemRequest>())
            .Select(i => new OrderLine(i.CatalogItemId, i.Quantity))
            .ToList();

        var order = await orders.PlaceOrderAsync(buyerId, lines, address);
        return Results.Created($"api/orders/{order.Id}", OrderResponse.From(order, _payPal.Currency));
    }
}

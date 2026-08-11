using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order from catalog items for the signed-in shopper. Prices come from the catalog;
/// the order starts awaiting payment. Returns the new order id as a top-level field.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderPaymentService>
{
    private readonly IPaymentConfiguration _paymentConfiguration;

    public CreateOrderEndpoint(IPaymentConfiguration paymentConfiguration)
    {
        _paymentConfiguration = paymentConfiguration;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IOrderPaymentService service) =>
            {
                request.BuyerId = user.GetBuyerId();
                return await HandleAsync(request, service);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderPaymentService service)
    {
        if (request.Items is null || request.Items.Count == 0)
        {
            return Results.BadRequest("An order must contain at least one item.");
        }

        var lines = request.Items.Select(i => new OrderLine(i.CatalogItemId, i.Quantity)).ToList();
        var order = await service.PlaceOrderAsync(request.BuyerId, lines, request.ShipToAddress?.ToAddress());

        var summary = OrderMapper.ToSummary(order, _paymentConfiguration.Currency);
        var response = new CreateOrderResponse
        {
            OrderId = order.Id,
            Status = summary.Status,
            Total = summary.Total,
            Currency = summary.Currency,
            Order = summary
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }
}

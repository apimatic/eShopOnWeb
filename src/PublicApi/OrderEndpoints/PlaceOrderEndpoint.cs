using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Settings;
using MinimalApi.Endpoint;
using System.Linq;
using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order from catalog items; it starts awaiting payment.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint
{
    private readonly IOrderPaymentService _payments;
    private readonly PayPalSettings _settings;

    public PlaceOrderEndpoint(IOrderPaymentService payments, PayPalSettings settings)
    {
        _payments = payments;
        _settings = settings;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (PlaceOrderRequest request, ClaimsPrincipal user) =>
            {
                return await HandleAsync(request, user);
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, ClaimsPrincipal user)
    {
        var buyerId = user.Identity?.Name
            ?? throw new System.InvalidOperationException("Authenticated caller has no identity.");

        var lines = request.Items.Select(i => new PlaceOrderLine(i.CatalogItemId, i.Quantity)).ToList();
        var shipTo = request.ShipTo is null
            ? null
            : new ApplicationCore.Entities.OrderAggregate.Address(
                request.ShipTo.Street, request.ShipTo.City, request.ShipTo.State, request.ShipTo.Country, request.ShipTo.ZipCode);

        var result = await _payments.PlaceOrderAsync(buyerId, lines, shipTo);
        var order = result.Order;

        var response = new PlaceOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total(),
            Currency = _settings.Currency,
            OrderDate = order.OrderDate
        };

        return Results.Created($"/api/my-orders", response);
    }
}

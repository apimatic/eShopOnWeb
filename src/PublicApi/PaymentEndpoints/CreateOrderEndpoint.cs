using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Places an order from catalog items. The order starts awaiting payment.
/// </summary>
public class CreateOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, FinixPaymentService payments, ClaimsPrincipal user, CancellationToken ct) =>
            {
                return await HandleAsync(request, payments, user, ct);
            })
            .Produces<CreateOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(
        CreateOrderRequest request,
        FinixPaymentService payments,
        ClaimsPrincipal user,
        CancellationToken ct = default)
    {
        var response = new CreateOrderResponse(request.CorrelationId());
        var buyerId = ClaimsHelper.GetBuyerId(user);

        if (request.Items is null || request.Items.Count == 0)
            throw new FinixPaymentException(400, "ORDER_EMPTY",
                "An order must contain at least one catalog item.");

        var lines = request.Items
            .Select(i => new OrderLineInput(i.CatalogItemId, i.Quantity))
            .ToList();

        var shipping = new ShippingAddressInput(
            request.Street,
            request.City,
            request.State,
            request.Country,
            request.ZipCode);

        var order = await payments.CreateOrderAsync(buyerId, lines, shipping, ct);

        response.OrderId = order.Id;
        response.Total = order.Total();
        response.TotalCents = (long)Math.Round(response.Total * 100m, MidpointRounding.AwayFromZero);
        response.Currency = order.Currency ?? string.Empty;
        response.PaymentStatus = order.PaymentStatus.ToString();

        return Results.Created($"api/orders/{order.Id}", response);
    }
}

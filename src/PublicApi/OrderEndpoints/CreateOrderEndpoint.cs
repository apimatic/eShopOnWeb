using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, ICheckoutPaymentService>
{
    private readonly IPayPalSettings _payPalSettings;

    public CreateOrderEndpoint(IPayPalSettings payPalSettings)
    {
        _payPalSettings = payPalSettings;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, ICheckoutPaymentService service, CancellationToken ct) =>
            {
                var lines = (request.Items ?? new List<CreateOrderLineRequest>())
                    .ConvertAll(i => new OrderLine(i.CatalogItemId, i.Quantity));
                var shipTo = (request.ShipTo ?? new ShippingAddressRequest()).ToAddress();
                var order = await service.PlaceOrderAsync(
                    user.GetBuyerId(),
                    lines,
                    shipTo,
                    _payPalSettings.Currency,
                    ct);
                return Results.Created($"api/orders/{order.Id}", OrderResponse.From(order));
            })
            .Produces<OrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CreateOrderRequest request, ICheckoutPaymentService service) =>
        Task.FromResult(Results.StatusCode(StatusCodes.Status501NotImplemented));
}

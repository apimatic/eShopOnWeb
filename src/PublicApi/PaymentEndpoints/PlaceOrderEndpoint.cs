using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class PlaceOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                async (PlaceOrderRequest request, IPaymentApplicationService payments,
                    ClaimsPrincipal user, CancellationToken cancellationToken) =>
                {
                    try
                    {
                        var buyerId = PaymentEndpointHelpers.RequireBuyerId(user);
                        var lines = request.Items?
                            .Select(item => new OrderLineInput(item.CatalogItemId, item.Quantity))
                            .ToList() ?? new();

                        var orderId = await payments.CreateOrderAsync(
                            buyerId,
                            lines,
                            new ShippingAddressInput(
                                request.Street,
                                request.City,
                                request.State,
                                request.Country,
                                request.ZipCode),
                            cancellationToken);

                        return Results.Created(
                            $"api/orders/{orderId}",
                            new PlaceOrderResponse { OrderId = orderId });
                    }
                    catch (PaymentOperationException exception)
                    {
                        return PaymentEndpointHelpers.Fail(exception);
                    }
                })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .Produces<ApiErrorResponse>(StatusCodes.Status400BadRequest)
            .WithTags("PaymentEndpoints");
    }
}

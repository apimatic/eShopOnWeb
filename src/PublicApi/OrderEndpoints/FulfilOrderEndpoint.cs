using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: marks the order fulfilled and captures the money. A stale hold is renewed first.
/// Restricted to the administrator role.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, OperatorOrderRequest, IOrderPaymentService>
{
    private readonly PayPalSettings _settings;

    public FulfilOrderEndpoint(PayPalSettings settings) => _settings = settings;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, CancellationToken ct, IOrderPaymentService service) =>
            {
                return await HandleAsync(new OperatorOrderRequest(orderId, ct), service);
            })
            .Produces<OrderPaymentDto>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OperatorOrderRequest request, IOrderPaymentService service)
    {
        var order = await service.FulfilAsync(request.OrderId, request.Cancellation);
        return Results.Ok(OrderPaymentDto.From(order, _settings.Currency));
    }
}

/// <summary>An operator action on a single order, identified by route.</summary>
public record OperatorOrderRequest(int OrderId, CancellationToken Cancellation);

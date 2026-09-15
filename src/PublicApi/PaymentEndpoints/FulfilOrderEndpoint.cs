using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Operator action: marks an order fulfilled and captures the held funds (takes the money). A stale authorization
/// is renewed first; one that can no longer be renewed is reported in terms an operator can act on. Restricted to
/// the administrator role. Idempotent.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, OrderIdRequest>
{
    private readonly IPaymentService _payments;

    public FulfilOrderEndpoint(IPaymentService payments)
    {
        _payments = payments;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId) => await HandleAsync(new OrderIdRequest(orderId)))
            .Produces<PaymentActionResponse>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderIdRequest request)
    {
        var payment = await _payments.FulfilAsync(request.OrderId);
        return Results.Ok(new PaymentActionResponse(payment.ToDto()));
    }
}

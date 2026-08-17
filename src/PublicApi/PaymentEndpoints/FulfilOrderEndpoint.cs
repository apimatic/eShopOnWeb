using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;
using static Microsoft.eShopWeb.PublicApi.PaymentEndpoints.PaymentApiHelpers;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// POST /api/orders/{orderId}/fulfil — operator action. Marks the order fulfilled and captures the money.
/// A stale authorization is renewed rather than failing; one that can no longer be renewed is reported in
/// operator-actionable terms. Restricted to the administrator role.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest>
{
    private readonly IPaymentService _paymentService;

    public FulfilOrderEndpoint(IPaymentService paymentService) => _paymentService = paymentService;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId) => await HandleAsync(new FulfilOrderRequest(orderId)))
            .Produces<PaymentView>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(FulfilOrderRequest request)
    {
        var result = await _paymentService.FulfilAsync(request.OrderId);
        return ToHttp(result, view => Results.Ok(view));
    }
}

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
/// POST /api/payment-methods — save (vault) a card for the signed-in shopper. The response
/// identifies the saved card (top-level <c>paymentMethodId</c>) and describes it safely (brand +
/// last four) — never full card details, which are never stored by this app.
/// </summary>
public class SavePaymentMethodEndpoint : PaymentEndpointBase, IEndpoint<IResult, SavePaymentMethodRequest, IPaymentService>
{
    public SavePaymentMethodEndpoint(IHttpContextAccessor httpContextAccessor) : base(httpContextAccessor) { }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, IPaymentService paymentService) =>
                await HandleAsync(request, paymentService))
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, IPaymentService paymentService)
    {
        var command = new SaveCardCommand(request.Card.ToGatewayCard(), request.Alias);
        var view = await paymentService.SaveCardAsync(BuyerId, command, RequestAborted);
        return Results.Created($"api/payment-methods/{view.PaymentMethodId}", view);
    }
}

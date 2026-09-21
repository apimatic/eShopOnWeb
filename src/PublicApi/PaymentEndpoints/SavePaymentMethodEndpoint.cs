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
/// POST /api/payment-methods — save (vault) a card for the signed-in shopper. Returns the saved-card
/// id and a safe description (brand + last four); never full card details. Returns the payment method id.
/// </summary>
public class SavePaymentMethodEndpoint : PaymentEndpointBase, IEndpoint<IResult, CreatePaymentMethodRequest, IPaymentService>
{
    public SavePaymentMethodEndpoint(IHttpContextAccessor httpContextAccessor) : base(httpContextAccessor) { }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, IPaymentService service) => await HandleAsync(request, service))
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, IPaymentService service)
    {
        if (request.Card is null)
            return Results.BadRequest(new { message = "Card details are required." });

        var card = PaymentMappings.ToCardDetails(request.Card);
        var saved = await service.SaveCardAsync(BuyerId, card, RequestAborted);
        return Results.Created($"api/payment-methods/{saved.PaymentMethodId}", new CreatePaymentMethodResponse
        {
            PaymentMethodId = saved.PaymentMethodId,
            Brand = saved.Brand,
            LastFourDigits = saved.LastFourDigits,
            Expiry = saved.Expiry
        });
    }
}

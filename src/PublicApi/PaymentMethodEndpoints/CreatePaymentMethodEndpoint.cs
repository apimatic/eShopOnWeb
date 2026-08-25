using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.PayPalService;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodRequest : BaseRequest
{
    public string CardNumber { get; set; } = "";
    public string CardExpiry { get; set; } = "";
    public string CardSecurityCode { get; set; } = "";
    public string? CardHolderName { get; set; }
}

public class CreatePaymentMethodResponse : BaseResponse
{
    public CreatePaymentMethodResponse(System.Guid correlationId) : base(correlationId) { }
    public int PaymentMethodId { get; set; }
    public string? Last4 { get; set; }
    public string? Brand { get; set; }
    public string? Expiry { get; set; }
}

public class CreatePaymentMethodEndpoint : IEndpoint<IResult>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreatePaymentMethodRequest request, IRepository<Buyer> buyerRepo,
                   IPayPalService paypal, HttpContext httpContext, CancellationToken ct) =>
            {
                var buyerId = httpContext.User.FindFirst(ClaimTypes.Name)?.Value;
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

                if (string.IsNullOrEmpty(request.CardNumber) || string.IsNullOrEmpty(request.CardExpiry)
                    || string.IsNullOrEmpty(request.CardSecurityCode))
                    return Results.BadRequest("CardNumber, CardExpiry, and CardSecurityCode are required.");

                // Use the buyer's username as the stable PayPal customer ID
                var vaultRequest = new CardVaultRequest(
                    request.CardNumber, request.CardExpiry, request.CardSecurityCode, request.CardHolderName);

                var vaultId = await paypal.VaultCardAsync(buyerId, vaultRequest, ct);
                var cards = await paypal.ListCardsAsync(buyerId, ct);
                var savedCard = cards.FirstOrDefault(c => c.PaymentMethodId == vaultId);

                // Persist in eShop Buyer aggregate
                var buyer = await buyerRepo.FirstOrDefaultAsync(new BuyerByIdentitySpec(buyerId), ct);
                if (buyer == null)
                {
                    buyer = new Buyer(buyerId);
                    var pm = buyer.AddPaymentMethod(vaultId, savedCard?.Last4, savedCard?.Brand, savedCard?.Expiry);
                    await buyerRepo.AddAsync(buyer, ct);
                    return Results.Created($"api/payment-methods/{pm.Id}",
                        new CreatePaymentMethodResponse(request.CorrelationId())
                        {
                            PaymentMethodId = pm.Id,
                            Last4 = pm.Last4,
                            Brand = pm.Brand,
                            Expiry = pm.Expiry
                        });
                }
                else
                {
                    var pm = buyer.AddPaymentMethod(vaultId, savedCard?.Last4, savedCard?.Brand, savedCard?.Expiry);
                    await buyerRepo.UpdateAsync(buyer, ct);
                    return Results.Created($"api/payment-methods/{pm.Id}",
                        new CreatePaymentMethodResponse(request.CorrelationId())
                        {
                            PaymentMethodId = pm.Id,
                            Last4 = pm.Last4,
                            Brand = pm.Brand,
                            Expiry = pm.Expiry
                        });
                }
            })
            .Produces<CreatePaymentMethodResponse>(201)
            .Produces(400)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync() => Task.FromResult<IResult>(Results.StatusCode(501));
}

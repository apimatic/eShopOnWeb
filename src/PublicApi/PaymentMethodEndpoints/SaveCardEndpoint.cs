using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class SaveCardEndpoint : IEndpoint<IResult, SaveCardRequest, IRepository<Buyer>>
{
    private readonly IPaymentService _paymentService;

    public SaveCardEndpoint(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SaveCardRequest request, IRepository<Buyer> buyerRepo, HttpContext httpContext) =>
            {
                request.BuyerId = httpContext.User.Identity!.Name!;
                return await HandleAsync(request, buyerRepo);
            })
            .Produces<SaveCardResponse>(201)
            .ProducesProblem(400)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SaveCardRequest request, IRepository<Buyer> buyerRepo)
    {
        if (string.IsNullOrWhiteSpace(request.Number) || string.IsNullOrWhiteSpace(request.Expiry))
            return Results.BadRequest("Card number and expiry are required.");

        var spec = new BuyerWithPaymentMethodsSpecification(request.BuyerId);
        var buyer = (await buyerRepo.ListAsync(spec)).FirstOrDefault();
        if (buyer is null)
        {
            buyer = new Buyer(request.BuyerId);
            buyer = await buyerRepo.AddAsync(buyer);
        }

        var card = new CardDetails(request.Number, request.Expiry, request.Cvv, request.Name);
        var saved = await _paymentService.SaveCardAsync(request.BuyerId, card);

        var method = buyer.AddPaymentMethod(
            saved.VaultToken, saved.Last4, saved.CardBrand,
            saved.ExpiryMonth, saved.ExpiryYear, request.Alias);
        await buyerRepo.UpdateAsync(buyer);

        // Re-query to get the generated ID
        var updated = (await buyerRepo.ListAsync(spec)).FirstOrDefault();
        var pm = updated?.PaymentMethods.LastOrDefault();

        return Results.Created($"api/payment-methods/{pm?.Id}", new SaveCardResponse(request.CorrelationId())
        {
            PaymentMethodId = pm?.Id ?? 0,
            Last4 = saved.Last4,
            CardBrand = saved.CardBrand,
            ExpiryMonth = saved.ExpiryMonth,
            ExpiryYear = saved.ExpiryYear
        });
    }
}

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

public class DeleteCardEndpoint : IEndpoint<IResult, int, IRepository<Buyer>>
{
    private readonly IPaymentService _paymentService;

    public DeleteCardEndpoint(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, IRepository<Buyer> buyerRepo, HttpContext httpContext) =>
            {
                return await HandleAsync(paymentMethodId, buyerRepo, httpContext.User.Identity!.Name!);
            })
            .Produces(204)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(int paymentMethodId, IRepository<Buyer> buyerRepo)
        => Task.FromResult(Results.NoContent());

    private async Task<IResult> HandleAsync(int paymentMethodId, IRepository<Buyer> buyerRepo, string buyerId)
    {
        var spec = new BuyerWithPaymentMethodsSpecification(buyerId);
        var buyer = (await buyerRepo.ListAsync(spec)).FirstOrDefault();
        if (buyer is null) return Results.NotFound();

        var pm = buyer.PaymentMethods.FirstOrDefault(p => p.Id == paymentMethodId);
        if (pm is null) return Results.NotFound();

        if (!string.IsNullOrEmpty(pm.VaultToken))
            await _paymentService.DeleteSavedCardAsync(pm.VaultToken);

        buyer.RemovePaymentMethod(paymentMethodId);
        await buyerRepo.UpdateAsync(buyer);
        return Results.NoContent();
    }
}

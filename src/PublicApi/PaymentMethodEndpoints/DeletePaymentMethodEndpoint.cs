using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using Microsoft.eShopWeb.PublicApi.PaymentShared;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class DeletePaymentMethodRequest : BaseRequest
{
    public DeletePaymentMethodRequest(int paymentMethodId, string buyerId)
    {
        PaymentMethodId = paymentMethodId;
        BuyerId = buyerId;
    }

    public int PaymentMethodId { get; }
    public string BuyerId { get; }
}

/// <summary>
/// Removes one of the signed-in shopper's own saved cards. Deletes PayPal's vault token too,
/// so it can no longer be used to pay even if referenced by id.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, PaymentDependencies>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ClaimsPrincipal user,
             IRepository<Order> orderRepository, IRepository<Payment> paymentRepository, IRepository<Buyer> buyerRepository,
             IRepository<CatalogItem> catalogItemRepository, IPayPalClient payPalClient, IOptions<PayPalOptions> payPalOptions) =>
            {
                var request = new DeletePaymentMethodRequest(paymentMethodId, user.Identity!.Name!);
                var deps = new PaymentDependencies(orderRepository, paymentRepository, buyerRepository, catalogItemRepository, payPalClient, payPalOptions.Value);
                return await HandleAsync(request, deps);
            })
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, PaymentDependencies deps)
    {
        var buyer = await deps.BuyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(request.BuyerId));
        var paymentMethod = buyer?.PaymentMethods.FirstOrDefault(p => p.Id == request.PaymentMethodId);
        if (buyer == null || paymentMethod == null)
        {
            return Results.NotFound();
        }

        try
        {
            await deps.PayPalClient.DeleteVaultedPaymentTokenAsync(paymentMethod.CardId);
        }
        catch (PayPalApiException ex)
        {
            return Results.Problem(ex.Message, statusCode: 502, title: ex.ErrorName ?? "Could not remove card from PayPal");
        }

        buyer.RemovePaymentMethod(request.PaymentMethodId);
        await deps.BuyerRepository.UpdateAsync(buyer);

        return Results.NoContent();
    }
}

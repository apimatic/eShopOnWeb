using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class SavePaymentMethodRequest
{
    public string CardNumber { get; set; } = "";
    public string Expiry { get; set; } = "";
    public string CardholderName { get; set; } = "";
    public BillingAddressRequest BillingAddress { get; set; } = new();
}

public class BillingAddressRequest
{
    public string Street { get; set; } = "";
    public string City { get; set; } = "";
    public string State { get; set; } = "";
    public string Country { get; set; } = "";
    public string ZipCode { get; set; } = "";
}

public class SavePaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public string Last4 { get; set; } = "";
    public string Brand { get; set; } = "";
    public string Expiry { get; set; } = "";
}

public class SavePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SavePaymentMethodRequest request, HttpContext ctx,
                   IRepository<SavedPaymentMethod> pmRepo,
                   PayPalClient paypal) =>
            {
                var buyerId = ctx.User.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();

                if (string.IsNullOrWhiteSpace(request.CardNumber) ||
                    string.IsNullOrWhiteSpace(request.Expiry) ||
                    string.IsNullOrWhiteSpace(request.CardholderName) ||
                    request.BillingAddress == null)
                {
                    return Results.BadRequest(new { error = "cardNumber, expiry, cardholderName, and billingAddress are required." });
                }

                var addr = request.BillingAddress;
                PayPalVaultResult vaultResult;
                try
                {
                    vaultResult = await paypal.VaultCardAsync(
                        request.CardNumber, request.Expiry, request.CardholderName,
                        addr.Street, addr.City, addr.State, addr.Country, addr.ZipCode);
                }
                catch (PayPalException ex)
                {
                    return Results.UnprocessableEntity(new
                    {
                        error = $"Failed to save card: {ex.Message}",
                        paypalCode = ex.PayPalName
                    });
                }

                var pm = new SavedPaymentMethod(
                    buyerId,
                    vaultResult.CustomerId,
                    vaultResult.VaultId,
                    vaultResult.Last4,
                    vaultResult.Brand,
                    vaultResult.Expiry);

                pm = await pmRepo.AddAsync(pm);

                return Results.Created($"/api/payment-methods/{pm.Id}", new SavePaymentMethodResponse
                {
                    PaymentMethodId = pm.Id,
                    Last4 = pm.Last4,
                    Brand = pm.Brand,
                    Expiry = pm.Expiry
                });
            })
            .Produces<SavePaymentMethodResponse>(201)
            .ProducesProblem(400)
            .ProducesProblem(422)
            .WithTags("PaymentMethodEndpoints");
    }
}

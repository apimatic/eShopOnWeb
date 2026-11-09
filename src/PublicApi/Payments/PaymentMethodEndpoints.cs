using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public record SavePaymentMethodRequest(
    string? Type,
    string? PersonName,
    string? CardNumber,
    string? CardCvv,
    string? CardExp,
    string? CardType,
    string? BankAccount,
    string? BankRouting,
    string? BankName,
    string? BankType,
    string? BankHolderType,
    string? Address1,
    string? Address2,
    string? City,
    string? State,
    string? Zip,
    string? Country);

/// <summary>Saves a card or a bank account for the calling shopper.</summary>
public class SavePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, ClaimsPrincipal user, ShopperAccessor shopper, StaxClient stax, StaxCustomers customers, CancellationToken ct) =>
            {
                var email = await shopper.EmailAsync(user);
                var type = request.Type?.Trim().ToLowerInvariant();
                object body;
                if (string.IsNullOrWhiteSpace(request.PersonName)) throw new ApiException(400, "personName is required.");
                if (type == "card")
                {
                    if (string.IsNullOrWhiteSpace(request.CardNumber) || string.IsNullOrWhiteSpace(request.CardCvv) || string.IsNullOrWhiteSpace(request.CardExp))
                        throw new ApiException(400, "cardNumber, cardCvv and cardExp (MMYY) are required for a card.");
                    var customerId = await customers.FindOrCreateAsync(email, null, ct);
                    body = new
                    {
                        customer_id = customerId,
                        method = "card",
                        person_name = request.PersonName,
                        card_number = request.CardNumber,
                        card_cvv = request.CardCvv,
                        card_exp = request.CardExp,
                        card_type = request.CardType,
                        address_1 = request.Address1,
                        address_2 = request.Address2,
                        address_city = request.City,
                        address_state = request.State,
                        address_zip = request.Zip,
                        address_country = request.Country
                    };
                }
                else if (type == "bank")
                {
                    if (string.IsNullOrWhiteSpace(request.BankAccount) || string.IsNullOrWhiteSpace(request.BankRouting))
                        throw new ApiException(400, "bankAccount and bankRouting are required for a bank account.");
                    var customerId = await customers.FindOrCreateAsync(email, null, ct);
                    body = new
                    {
                        customer_id = customerId,
                        method = "bank",
                        person_name = request.PersonName,
                        bank_account = request.BankAccount,
                        bank_routing = request.BankRouting,
                        bank_name = string.IsNullOrWhiteSpace(request.BankName) ? "Bank" : request.BankName,
                        bank_type = string.IsNullOrWhiteSpace(request.BankType) ? "checking" : request.BankType!.ToLowerInvariant(),
                        bank_holder_type = string.IsNullOrWhiteSpace(request.BankHolderType) ? "personal" : request.BankHolderType!.ToLowerInvariant(),
                        address_1 = request.Address1,
                        address_2 = request.Address2,
                        address_city = request.City,
                        address_state = request.State,
                        address_zip = request.Zip,
                        address_country = request.Country
                    };
                }
                else
                {
                    throw new ApiException(400, "type must be \"card\" or \"bank\".");
                }

                var created = await stax.PostAsync("payment-method", body, ct);
                return Results.Created($"api/payment-methods/{(string?)created["id"]}", StaxCustomers.ToView(created));
            })
            .WithTags("PaymentMethodEndpoints");
    }
}

/// <summary>Lists the calling shopper's saved payment methods.</summary>
public class ListPaymentMethodsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ShopperAccessor shopper, StaxCustomers customers, CancellationToken ct) =>
            {
                var email = await shopper.EmailAsync(user);
                var result = new System.Collections.Generic.List<object>();
                foreach (var c in await customers.FindAsync(email, ct))
                {
                    foreach (var m in await customers.ListMethodsAsync((string)c["id"]!, false, ct))
                        result.Add(StaxCustomers.ToView(m));
                }
                return Results.Ok(new { paymentMethods = result });
            })
            .WithTags("PaymentMethodEndpoints");
    }
}

/// <summary>Removes one of the calling shopper's saved payment methods.</summary>
public class DeletePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string paymentMethodId, ClaimsPrincipal user, ShopperAccessor shopper, StaxClient stax, StaxCustomers customers, CancellationToken ct) =>
            {
                var email = await shopper.EmailAsync(user);
                await customers.GetOwnedActiveMethodAsync(email, paymentMethodId, ct);
                await stax.DeleteAsync("payment-method/" + StaxClient.Q(paymentMethodId), ct);
                return Results.NoContent();
            })
            .WithTags("PaymentMethodEndpoints");
    }
}

/// <summary>Support view: every payment method a shopper ever saved, removed ones included.</summary>
public class AdminShopperPaymentMethodsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/admin/shoppers/{email}/payment-methods",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string email, StaxCustomers customers, CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(email)) throw new ApiException(400, "email is required.");
                var result = new System.Collections.Generic.List<(string? created, object view)>();
                foreach (var c in await customers.FindAsync(email.Trim(), ct))
                {
                    var id = (string)c["id"]!;
                    foreach (var m in await customers.ListMethodsAsync(id, false, ct))
                        result.Add(((string?)m["created_at"], StaxCustomers.ToView(m, true)));
                    foreach (var m in await customers.ListMethodsAsync(id, true, ct))
                        result.Add(((string?)m["created_at"], StaxCustomers.ToView(m, true)));
                }
                return Results.Ok(new { email, paymentMethods = result.OrderBy(r => r.created).Select(r => r.view).ToList() });
            })
            .WithTags("PaymentMethodEndpoints");
    }
}

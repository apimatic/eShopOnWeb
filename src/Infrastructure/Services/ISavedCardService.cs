using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public record SaveCardRequest(
    string CardNumber,
    int CardExpiryMonth,
    int CardExpiryYear,
    string? Cvv,
    string? CardholderName,
    string BillingCountryCode,
    string? BillingPostalCode);

public record SavedCardResult(
    int PaymentMethodId,
    string LastFour,
    string Brand,
    string Expiry,
    string? CardholderName);

public interface ISavedCardService
{
    Task<SavedCardResult> SaveCardAsync(string buyerId, SaveCardRequest request);
    Task<List<SavedCardResult>> GetSavedCardsAsync(string buyerId);
    Task DeleteSavedCardAsync(int paymentMethodId, string buyerId);
}

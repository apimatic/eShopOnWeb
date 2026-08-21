using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISavedCardService
{
    Task<SavedCardDto> SaveCardAsync(string buyerId, CardPaymentCommand card, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedCardDto>> ListAsync(string buyerId, CancellationToken cancellationToken = default);

    Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
}

public sealed class SavedCardDto
{
    public int PaymentMethodId { get; init; }
    public string LastDigits { get; init; } = string.Empty;
    public string? Brand { get; init; }
    public string? Expiry { get; init; }
    public string? CardholderName { get; init; }
    public string DisplayName { get; init; } = string.Empty;
}

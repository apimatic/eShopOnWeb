using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface IMaxioClient
{
    Task<List<MaxioProduct>> GetProductsAsync(string? productFamilyHandle = null, CancellationToken ct = default);
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken ct = default);
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerCreate customer, CancellationToken ct = default);
    Task<MaxioPaymentProfile> CreatePaymentProfileAsync(MaxioPaymentProfileCreate profile, CancellationToken ct = default);
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioSubscriptionCreate subscription, CancellationToken ct = default);
    Task<List<MaxioSubscription>> GetSubscriptionsByCustomerIdAsync(int customerId, CancellationToken ct = default);
    Task<MaxioProduct?> GetProductByHandleAsync(string handle, CancellationToken ct = default);
}

public class MaxioProduct
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public int PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public bool RequireCreditCard { get; set; }
    public bool Taxable { get; set; }
    public MaxioProductFamily? ProductFamily { get; set; }
    public int? ProductPricePointId { get; set; }
    public string? ProductPricePointHandle { get; set; }
}

public class MaxioProductFamily
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
}

public class MaxioCustomer
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public string? Organization { get; set; }
}

public class MaxioCustomerCreate
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Reference { get; set; }
}

public class MaxioSubscription
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string? CurrentPeriodEndsAt { get; set; }
    public string? NextAssessmentAt { get; set; }
    public string? ActivatedAt { get; set; }
    public string? CreatedAt { get; set; }
    public MaxioProduct? Product { get; set; }
    public MaxioCustomer? Customer { get; set; }
    public int? ProductPricePointId { get; set; }
}

public class MaxioSubscriptionCreate
{
    public string ProductHandle { get; set; } = string.Empty;
    public int? CustomerId { get; set; }
    public int? PaymentProfileId { get; set; }
    public MaxioCustomerCreate? CustomerAttributes { get; set; }
}

public class MaxioPaymentProfile
{
    public int Id { get; set; }
    public string PaymentType { get; set; } = string.Empty;
    public string? MaskedCardNumber { get; set; }
    public string? CardType { get; set; }
    public int? CustomerId { get; set; }
}

public class MaxioPaymentProfileCreate
{
    public int CustomerId { get; set; }
    public string PaymentType { get; set; } = "credit_card";
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FullNumber { get; set; } = string.Empty;
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string CurrentVault { get; set; } = "bogus";
    public string VaultToken { get; set; } = "1";
}

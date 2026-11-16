using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public record OrderLineCommand(int CatalogItemId, int Quantity);

public record ShippingAddressCommand(string Street, string City, string? State, string Country, string ZipCode);

public record CreateOrderCommand(string BuyerId, IReadOnlyList<OrderLineCommand> Items,
    ShippingAddressCommand? ShippingAddress);

public record CardAddress(string Line1, string? Line2, string City, string Region, string PostalCode,
    string Country);

public record CardDetails(string Number, int ExpirationMonth, int ExpirationYear, string? SecurityCode,
    string Name, CardAddress? Address);

public record PayOrderCommand(int? PaymentMethodId, CardDetails? Card);

public record ReconciliationEntry(string FinixId, string Kind, string? Type, string? State,
    long? AmountCents, string? Currency, System.DateTimeOffset? CreatedAt, int? OrderId, string? BuyerId,
    bool Matched, string? Note);

public record ReconciliationReport(System.DateTimeOffset From, System.DateTimeOffset To,
    int FinixTransactionCount, int MatchedCount, int FinixOnlyCount, int EshopOnlyCount,
    IReadOnlyList<ReconciliationEntry> Entries);

using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Banking.Plaid;

// Only the parts of Plaid's responses this app uses. Property names map to Plaid's snake_case.

public class PlaidLinkTokenResponse
{
    public string LinkToken { get; set; } = string.Empty;
    public string? Expiration { get; set; }
}

public class PlaidExchangeResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
}

public class PlaidItemResponse
{
    public PlaidItem Item { get; set; } = new();
}

public class PlaidItem
{
    public string ItemId { get; set; } = string.Empty;
    public string? InstitutionId { get; set; }
    public string? InstitutionName { get; set; }
    public List<string>? Products { get; set; }
    public List<string>? BilledProducts { get; set; }
    public List<string>? ConsentedProducts { get; set; }
}

public class PlaidAccountsResponse
{
    public List<PlaidAccount> Accounts { get; set; } = new();
}

public class PlaidAccount
{
    public string AccountId { get; set; } = string.Empty;
    public string? PersistentAccountId { get; set; }
    public string? Name { get; set; }
    public string? OfficialName { get; set; }
    public string? Mask { get; set; }
    public string? Type { get; set; }
    public string? Subtype { get; set; }
}

public class PlaidInstitutionsResponse
{
    public List<PlaidInstitution> Institutions { get; set; } = new();
    public int Total { get; set; }
}

public class PlaidInstitution
{
    public string InstitutionId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<string>? Products { get; set; }
    public List<string>? CountryCodes { get; set; }
    public bool Oauth { get; set; }
    public string? Url { get; set; }
}

public class PlaidTransactionsResponse
{
    public List<PlaidTransaction> Transactions { get; set; } = new();
    public int TotalTransactions { get; set; }
}

public class PlaidTransaction
{
    public string TransactionId { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? MerchantName { get; set; }
    public decimal Amount { get; set; }
    public string? IsoCurrencyCode { get; set; }
    public string? UnofficialCurrencyCode { get; set; }
    public string Date { get; set; } = string.Empty;
    public bool Pending { get; set; }
    public string? PendingTransactionId { get; set; }
}

public class PlaidSyncResponse
{
    public List<PlaidTransaction> Added { get; set; } = new();
    public List<PlaidTransaction> Modified { get; set; } = new();
    public List<PlaidRemovedTransaction> Removed { get; set; } = new();
    public string NextCursor { get; set; } = string.Empty;
    public bool HasMore { get; set; }
}

public class PlaidRemovedTransaction
{
    public string TransactionId { get; set; } = string.Empty;
}

public class PlaidIdentityResponse
{
    public List<PlaidIdentityAccount> Accounts { get; set; } = new();
}

public class PlaidIdentityAccount
{
    public string AccountId { get; set; } = string.Empty;
    public List<PlaidOwner>? Owners { get; set; }
}

public class PlaidOwner
{
    public List<string>? Names { get; set; }
}

public class PlaidAuthorizationResponse
{
    public PlaidAuthorization Authorization { get; set; } = new();
}

public class PlaidAuthorization
{
    public string Id { get; set; } = string.Empty;
    public string Decision { get; set; } = string.Empty;
    public PlaidDecisionRationale? DecisionRationale { get; set; }
}

public class PlaidDecisionRationale
{
    public string? Code { get; set; }
    public string? Description { get; set; }
}

public class PlaidTransferResponse
{
    public PlaidTransfer Transfer { get; set; } = new();
}

public class PlaidTransfer
{
    public string Id { get; set; } = string.Empty;
    public string? Status { get; set; }
    public bool Cancellable { get; set; }
    public string? Amount { get; set; }
    public string? ExpectedFundsAvailableDate { get; set; }
    public List<PlaidTransferRefund>? Refunds { get; set; }
}

public class PlaidTransferRefund
{
    public string Id { get; set; } = string.Empty;
    public string? Amount { get; set; }
    public string? Status { get; set; }
}

public class PlaidRefundResponse
{
    public PlaidTransferRefund Refund { get; set; } = new();
}

public class PlaidHoldingsResponse
{
    public List<PlaidAccount> Accounts { get; set; } = new();
    public List<PlaidHolding> Holdings { get; set; } = new();
    public List<PlaidSecurity> Securities { get; set; } = new();
}

public class PlaidHolding
{
    public string AccountId { get; set; } = string.Empty;
    public string SecurityId { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal? InstitutionPrice { get; set; }
    public decimal? InstitutionValue { get; set; }
    public string? IsoCurrencyCode { get; set; }
    public string? UnofficialCurrencyCode { get; set; }
}

public class PlaidSecurity
{
    public string SecurityId { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? TickerSymbol { get; set; }
    public string? Type { get; set; }
    public object? OptionContract { get; set; }
}

public class PlaidEmpty
{
}

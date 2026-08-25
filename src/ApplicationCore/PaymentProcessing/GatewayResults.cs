using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;

public record AuthorizationResult(string PayPalOrderId, string AuthorizationId, string Status, DateTimeOffset? ExpiresAt);

public record ReauthorizationResult(string AuthorizationId, string Status, DateTimeOffset? ExpiresAt);

public record CaptureResult(string CaptureId, string Status, decimal CapturedAmount, decimal? PayPalFeeAmount, decimal? NetAmount, DateTimeOffset CapturedAt);

public record RefundResult(string RefundId, string Status, decimal Amount);

public record SavedCardResult(string VaultId, string? Brand, string? Last4, int? ExpiryMonth, int? ExpiryYear);

public record TransactionRecord(string? TransactionId, decimal? Amount, string? Currency, string? Status, DateTimeOffset? Date);

public record ReconciliationReport(DateTimeOffset From, DateTimeOffset To, IReadOnlyList<TransactionRecord> PayPalTransactions, IReadOnlyList<ReconciledOrder> MatchedOrders, IReadOnlyList<TransactionRecord> UnmatchedPayPalTransactions, IReadOnlyList<ReconciledOrder> UnmatchedLocalOrders);

public record ReconciledOrder(int OrderId, string? CaptureId, decimal? CapturedAmount, string? CaptureStatus, DateTimeOffset? CapturedAt);

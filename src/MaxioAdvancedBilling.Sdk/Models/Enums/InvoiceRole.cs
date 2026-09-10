using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<InvoiceRole>))]
public sealed record InvoiceRole : StringEnum<InvoiceRole>
{
    private InvoiceRole(string value) : base(value)
    {
    }

    public static readonly InvoiceRole Unset = new("unset");

    public static readonly InvoiceRole Signup = new("signup");

    public static readonly InvoiceRole Renewal = new("renewal");

    public static readonly InvoiceRole Usage = new("usage");

    public static readonly InvoiceRole Reactivation = new("reactivation");

    public static readonly InvoiceRole Proration = new("proration");

    public static readonly InvoiceRole Migration = new("migration");

    public static readonly InvoiceRole Adhoc = new("adhoc");

    public static readonly InvoiceRole Backport = new("backport");

    public static readonly InvoiceRole BackportBalanceReconciliation = new("backport-balance-reconciliation");

    public static InvoiceRole FromValue(string value) => FromValueCore(value);
}

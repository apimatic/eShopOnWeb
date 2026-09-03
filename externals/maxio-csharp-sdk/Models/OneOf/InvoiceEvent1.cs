using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models.OneOf;

[JsonConverter(typeof(InvoiceEvent1Converter))]
public record InvoiceEvent1
{
    private readonly Optional<ApplyCreditNoteEvent> _applyCreditNoteEventValue;

    private readonly Optional<ApplyDebitNoteEvent> _applyDebitNoteEventValue;

    private readonly Optional<ApplyPaymentEvent> _applyPaymentEventValue;

    private readonly Optional<BackportInvoiceEvent> _backportInvoiceEventValue;

    private readonly Optional<ChangeChargebackStatusEvent> _changeChargebackStatusEventValue;

    private readonly Optional<ChangeInvoiceCollectionMethodEvent> _changeInvoiceCollectionMethodEventValue;

    private readonly Optional<ChangeInvoiceStatusEvent> _changeInvoiceStatusEventValue;

    private readonly Optional<CreateCreditNoteEvent> _createCreditNoteEventValue;

    private readonly Optional<CreateDebitNoteEvent> _createDebitNoteEventValue;

    private readonly Optional<FailedPaymentEvent> _failedPaymentEventValue;

    private readonly Optional<IssueInvoiceEvent> _issueInvoiceEventValue;

    private readonly Optional<RefundInvoiceEvent> _refundInvoiceEventValue;

    private readonly Optional<RemovePaymentEvent> _removePaymentEventValue;

    private readonly Optional<VoidInvoiceEvent> _voidInvoiceEventValue;

    private readonly Optional<VoidRemainderEvent> _voidRemainderEventValue;

    private InvoiceEvent1(Optional<ApplyCreditNoteEvent> applyCreditNoteEventValue,
        Optional<ApplyDebitNoteEvent> applyDebitNoteEventValue,
        Optional<ApplyPaymentEvent> applyPaymentEventValue,
        Optional<BackportInvoiceEvent> backportInvoiceEventValue,
        Optional<ChangeChargebackStatusEvent> changeChargebackStatusEventValue,
        Optional<ChangeInvoiceCollectionMethodEvent> changeInvoiceCollectionMethodEventValue,
        Optional<ChangeInvoiceStatusEvent> changeInvoiceStatusEventValue,
        Optional<CreateCreditNoteEvent> createCreditNoteEventValue,
        Optional<CreateDebitNoteEvent> createDebitNoteEventValue,
        Optional<FailedPaymentEvent> failedPaymentEventValue,
        Optional<IssueInvoiceEvent> issueInvoiceEventValue,
        Optional<RefundInvoiceEvent> refundInvoiceEventValue,
        Optional<RemovePaymentEvent> removePaymentEventValue,
        Optional<VoidInvoiceEvent> voidInvoiceEventValue,
        Optional<VoidRemainderEvent> voidRemainderEventValue)
    {
        _applyCreditNoteEventValue = applyCreditNoteEventValue;
        _applyDebitNoteEventValue = applyDebitNoteEventValue;
        _applyPaymentEventValue = applyPaymentEventValue;
        _backportInvoiceEventValue = backportInvoiceEventValue;
        _changeChargebackStatusEventValue = changeChargebackStatusEventValue;
        _changeInvoiceCollectionMethodEventValue = changeInvoiceCollectionMethodEventValue;
        _changeInvoiceStatusEventValue = changeInvoiceStatusEventValue;
        _createCreditNoteEventValue = createCreditNoteEventValue;
        _createDebitNoteEventValue = createDebitNoteEventValue;
        _failedPaymentEventValue = failedPaymentEventValue;
        _issueInvoiceEventValue = issueInvoiceEventValue;
        _refundInvoiceEventValue = refundInvoiceEventValue;
        _removePaymentEventValue = removePaymentEventValue;
        _voidInvoiceEventValue = voidInvoiceEventValue;
        _voidRemainderEventValue = voidRemainderEventValue;
    }

    public static InvoiceEvent1 ApplyCreditNoteEvent(ApplyCreditNoteEvent value) =>
        new(Optional<ApplyCreditNoteEvent>.Some(value),
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default);

    public static InvoiceEvent1 ApplyDebitNoteEvent(ApplyDebitNoteEvent value) =>
        new(default,
            Optional<ApplyDebitNoteEvent>.Some(value),
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default);

    public static InvoiceEvent1 ApplyPaymentEvent(ApplyPaymentEvent value) =>
        new(default,
            default,
            Optional<ApplyPaymentEvent>.Some(value),
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default);

    public static InvoiceEvent1 BackportInvoiceEvent(BackportInvoiceEvent value) =>
        new(default,
            default,
            default,
            Optional<BackportInvoiceEvent>.Some(value),
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default);

    public static InvoiceEvent1 ChangeChargebackStatusEvent(ChangeChargebackStatusEvent value) =>
        new(default,
            default,
            default,
            default,
            Optional<ChangeChargebackStatusEvent>.Some(value),
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default);

    public static InvoiceEvent1 ChangeInvoiceCollectionMethodEvent(ChangeInvoiceCollectionMethodEvent value) =>
        new(default,
            default,
            default,
            default,
            default,
            Optional<ChangeInvoiceCollectionMethodEvent>.Some(value),
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default);

    public static InvoiceEvent1 ChangeInvoiceStatusEvent(ChangeInvoiceStatusEvent value) =>
        new(default,
            default,
            default,
            default,
            default,
            default,
            Optional<ChangeInvoiceStatusEvent>.Some(value),
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default);

    public static InvoiceEvent1 CreateCreditNoteEvent(CreateCreditNoteEvent value) =>
        new(default,
            default,
            default,
            default,
            default,
            default,
            default,
            Optional<CreateCreditNoteEvent>.Some(value),
            default,
            default,
            default,
            default,
            default,
            default,
            default);

    public static InvoiceEvent1 CreateDebitNoteEvent(CreateDebitNoteEvent value) =>
        new(default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            Optional<CreateDebitNoteEvent>.Some(value),
            default,
            default,
            default,
            default,
            default,
            default);

    public static InvoiceEvent1 FailedPaymentEvent(FailedPaymentEvent value) =>
        new(default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            Optional<FailedPaymentEvent>.Some(value),
            default,
            default,
            default,
            default,
            default);

    public static InvoiceEvent1 IssueInvoiceEvent(IssueInvoiceEvent value) =>
        new(default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            Optional<IssueInvoiceEvent>.Some(value),
            default,
            default,
            default,
            default);

    public static InvoiceEvent1 RefundInvoiceEvent(RefundInvoiceEvent value) =>
        new(default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            Optional<RefundInvoiceEvent>.Some(value),
            default,
            default,
            default);

    public static InvoiceEvent1 RemovePaymentEvent(RemovePaymentEvent value) =>
        new(default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            Optional<RemovePaymentEvent>.Some(value),
            default,
            default);

    public static InvoiceEvent1 VoidInvoiceEvent(VoidInvoiceEvent value) =>
        new(default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            Optional<VoidInvoiceEvent>.Some(value),
            default);

    public static InvoiceEvent1 VoidRemainderEvent(VoidRemainderEvent value) =>
        new(default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            Optional<VoidRemainderEvent>.Some(value));

    public bool TryGetApplyCreditNoteEvent(out ApplyCreditNoteEvent value) =>
        _applyCreditNoteEventValue.TryGetValue(out value);

    public bool TryGetApplyDebitNoteEvent(out ApplyDebitNoteEvent value) =>
        _applyDebitNoteEventValue.TryGetValue(out value);

    public bool TryGetApplyPaymentEvent(out ApplyPaymentEvent value) =>
        _applyPaymentEventValue.TryGetValue(out value);

    public bool TryGetBackportInvoiceEvent(out BackportInvoiceEvent value) =>
        _backportInvoiceEventValue.TryGetValue(out value);

    public bool TryGetChangeChargebackStatusEvent(out ChangeChargebackStatusEvent value) =>
        _changeChargebackStatusEventValue.TryGetValue(out value);

    public bool TryGetChangeInvoiceCollectionMethodEvent(out ChangeInvoiceCollectionMethodEvent value) =>
        _changeInvoiceCollectionMethodEventValue.TryGetValue(out value);

    public bool TryGetChangeInvoiceStatusEvent(out ChangeInvoiceStatusEvent value) =>
        _changeInvoiceStatusEventValue.TryGetValue(out value);

    public bool TryGetCreateCreditNoteEvent(out CreateCreditNoteEvent value) =>
        _createCreditNoteEventValue.TryGetValue(out value);

    public bool TryGetCreateDebitNoteEvent(out CreateDebitNoteEvent value) =>
        _createDebitNoteEventValue.TryGetValue(out value);

    public bool TryGetFailedPaymentEvent(out FailedPaymentEvent value) =>
        _failedPaymentEventValue.TryGetValue(out value);

    public bool TryGetIssueInvoiceEvent(out IssueInvoiceEvent value) =>
        _issueInvoiceEventValue.TryGetValue(out value);

    public bool TryGetRefundInvoiceEvent(out RefundInvoiceEvent value) =>
        _refundInvoiceEventValue.TryGetValue(out value);

    public bool TryGetRemovePaymentEvent(out RemovePaymentEvent value) =>
        _removePaymentEventValue.TryGetValue(out value);

    public bool TryGetVoidInvoiceEvent(out VoidInvoiceEvent value) =>
        _voidInvoiceEventValue.TryGetValue(out value);

    public bool TryGetVoidRemainderEvent(out VoidRemainderEvent value) =>
        _voidRemainderEventValue.TryGetValue(out value);

    public static implicit operator InvoiceEvent1(ApplyCreditNoteEvent value) => ApplyCreditNoteEvent(value);

    public static implicit operator InvoiceEvent1(ApplyDebitNoteEvent value) => ApplyDebitNoteEvent(value);

    public static implicit operator InvoiceEvent1(ApplyPaymentEvent value) => ApplyPaymentEvent(value);

    public static implicit operator InvoiceEvent1(BackportInvoiceEvent value) => BackportInvoiceEvent(value);

    public static implicit operator InvoiceEvent1(ChangeChargebackStatusEvent value) =>
        ChangeChargebackStatusEvent(value);

    public static implicit operator InvoiceEvent1(ChangeInvoiceCollectionMethodEvent value) =>
        ChangeInvoiceCollectionMethodEvent(value);

    public static implicit operator InvoiceEvent1(ChangeInvoiceStatusEvent value) =>
        ChangeInvoiceStatusEvent(value);

    public static implicit operator InvoiceEvent1(CreateCreditNoteEvent value) => CreateCreditNoteEvent(value);

    public static implicit operator InvoiceEvent1(CreateDebitNoteEvent value) => CreateDebitNoteEvent(value);

    public static implicit operator InvoiceEvent1(FailedPaymentEvent value) => FailedPaymentEvent(value);

    public static implicit operator InvoiceEvent1(IssueInvoiceEvent value) => IssueInvoiceEvent(value);

    public static implicit operator InvoiceEvent1(RefundInvoiceEvent value) => RefundInvoiceEvent(value);

    public static implicit operator InvoiceEvent1(RemovePaymentEvent value) => RemovePaymentEvent(value);

    public static implicit operator InvoiceEvent1(VoidInvoiceEvent value) => VoidInvoiceEvent(value);

    public static implicit operator InvoiceEvent1(VoidRemainderEvent value) => VoidRemainderEvent(value);
}

file sealed class InvoiceEvent1Converter : JsonConverter<InvoiceEvent1>
{
    public override InvoiceEvent1 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (!root.TryGetProperty("event_type", out var typeProperty))
        {
            throw new JsonException("Missing required 'event_type' discriminator field");
        }
        var discriminator = typeProperty.GetString();
        return discriminator switch
        {
            "apply_credit_note" => InvoiceEvent1.ApplyCreditNoteEvent(root.Deserialize<ApplyCreditNoteEvent>(options)!),
            "apply_debit_note" => InvoiceEvent1.ApplyDebitNoteEvent(root.Deserialize<ApplyDebitNoteEvent>(options)!),
            "apply_payment" => InvoiceEvent1.ApplyPaymentEvent(root.Deserialize<ApplyPaymentEvent>(options)!),
            "backport_invoice" => InvoiceEvent1.BackportInvoiceEvent(root.Deserialize<BackportInvoiceEvent>(options)!),
            "change_chargeback_status" => InvoiceEvent1.ChangeChargebackStatusEvent(root.Deserialize<ChangeChargebackStatusEvent>(options)!),
            "change_invoice_collection_method" => InvoiceEvent1.ChangeInvoiceCollectionMethodEvent(root.Deserialize<ChangeInvoiceCollectionMethodEvent>(options)!),
            "change_invoice_status" => InvoiceEvent1.ChangeInvoiceStatusEvent(root.Deserialize<ChangeInvoiceStatusEvent>(options)!),
            "create_credit_note" => InvoiceEvent1.CreateCreditNoteEvent(root.Deserialize<CreateCreditNoteEvent>(options)!),
            "create_debit_note" => InvoiceEvent1.CreateDebitNoteEvent(root.Deserialize<CreateDebitNoteEvent>(options)!),
            "failed_payment" => InvoiceEvent1.FailedPaymentEvent(root.Deserialize<FailedPaymentEvent>(options)!),
            "issue_invoice" => InvoiceEvent1.IssueInvoiceEvent(root.Deserialize<IssueInvoiceEvent>(options)!),
            "refund_invoice" => InvoiceEvent1.RefundInvoiceEvent(root.Deserialize<RefundInvoiceEvent>(options)!),
            "remove_payment" => InvoiceEvent1.RemovePaymentEvent(root.Deserialize<RemovePaymentEvent>(options)!),
            "void_invoice" => InvoiceEvent1.VoidInvoiceEvent(root.Deserialize<VoidInvoiceEvent>(options)!),
            "void_remainder" => InvoiceEvent1.VoidRemainderEvent(root.Deserialize<VoidRemainderEvent>(options)!),
            _ => throw new JsonException($"JSON does not match ApplyCreditNoteEvent or ApplyDebitNoteEvent or ApplyPaymentEvent or BackportInvoiceEvent or ChangeChargebackStatusEvent or ChangeInvoiceCollectionMethodEvent or ChangeInvoiceStatusEvent or CreateCreditNoteEvent or CreateDebitNoteEvent or FailedPaymentEvent or IssueInvoiceEvent or RefundInvoiceEvent or RemovePaymentEvent or VoidInvoiceEvent or VoidRemainderEvent schemas: {root.ToString()}")
        };
    }

    public override void Write(Utf8JsonWriter writer, InvoiceEvent1 value, JsonSerializerOptions options)
    {
        if (value.TryGetApplyCreditNoteEvent(out var applyCreditNoteEventValue))
        {
            JsonSerializer.Serialize(writer, applyCreditNoteEventValue, options);
        }
        else if (value.TryGetApplyDebitNoteEvent(out var applyDebitNoteEventValue))
        {
            JsonSerializer.Serialize(writer, applyDebitNoteEventValue, options);
        }
        else if (value.TryGetApplyPaymentEvent(out var applyPaymentEventValue))
        {
            JsonSerializer.Serialize(writer, applyPaymentEventValue, options);
        }
        else if (value.TryGetBackportInvoiceEvent(out var backportInvoiceEventValue))
        {
            JsonSerializer.Serialize(writer, backportInvoiceEventValue, options);
        }
        else if (value.TryGetChangeChargebackStatusEvent(out var changeChargebackStatusEventValue))
        {
            JsonSerializer.Serialize(writer, changeChargebackStatusEventValue, options);
        }
        else if (value.TryGetChangeInvoiceCollectionMethodEvent(out var changeInvoiceCollectionMethodEventValue))
        {
            JsonSerializer.Serialize(writer, changeInvoiceCollectionMethodEventValue, options);
        }
        else if (value.TryGetChangeInvoiceStatusEvent(out var changeInvoiceStatusEventValue))
        {
            JsonSerializer.Serialize(writer, changeInvoiceStatusEventValue, options);
        }
        else if (value.TryGetCreateCreditNoteEvent(out var createCreditNoteEventValue))
        {
            JsonSerializer.Serialize(writer, createCreditNoteEventValue, options);
        }
        else if (value.TryGetCreateDebitNoteEvent(out var createDebitNoteEventValue))
        {
            JsonSerializer.Serialize(writer, createDebitNoteEventValue, options);
        }
        else if (value.TryGetFailedPaymentEvent(out var failedPaymentEventValue))
        {
            JsonSerializer.Serialize(writer, failedPaymentEventValue, options);
        }
        else if (value.TryGetIssueInvoiceEvent(out var issueInvoiceEventValue))
        {
            JsonSerializer.Serialize(writer, issueInvoiceEventValue, options);
        }
        else if (value.TryGetRefundInvoiceEvent(out var refundInvoiceEventValue))
        {
            JsonSerializer.Serialize(writer, refundInvoiceEventValue, options);
        }
        else if (value.TryGetRemovePaymentEvent(out var removePaymentEventValue))
        {
            JsonSerializer.Serialize(writer, removePaymentEventValue, options);
        }
        else if (value.TryGetVoidInvoiceEvent(out var voidInvoiceEventValue))
        {
            JsonSerializer.Serialize(writer, voidInvoiceEventValue, options);
        }
        else if (value.TryGetVoidRemainderEvent(out var voidRemainderEventValue))
        {
            JsonSerializer.Serialize(writer, voidRemainderEventValue, options);
        }
        else
        {
            throw new JsonException($"{nameof(InvoiceEvent1)} contains no valid value to serialize.");
        }
    }
}

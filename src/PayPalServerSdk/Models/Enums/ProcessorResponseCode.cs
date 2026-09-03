using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// Processor response code for the non-PayPal payment processor errors.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ProcessorResponseCode>))]
public sealed record ProcessorResponseCode : StringEnum<ProcessorResponseCode>
{
    private ProcessorResponseCode(string value) : base(value)
    {
    }

    /// <summary>
    /// APPROVED.
    /// </summary>
    public static readonly ProcessorResponseCode _0000 = new("0000");

    /// <summary>
    /// CVV2_FAILURE_POSSIBLE_RETRY_WITH_CVV.
    /// </summary>
    public static readonly ProcessorResponseCode _00N7 = new("00N7");

    /// <summary>
    /// REFERRAL.
    /// </summary>
    public static readonly ProcessorResponseCode _0100 = new("0100");

    /// <summary>
    /// ACCOUNT_NOT_FOUND.
    /// </summary>
    public static readonly ProcessorResponseCode _0390 = new("0390");

    /// <summary>
    /// DO_NOT_HONOR.
    /// </summary>
    public static readonly ProcessorResponseCode _0500 = new("0500");

    /// <summary>
    /// UNAUTHORIZED_TRANSACTION.
    /// </summary>
    public static readonly ProcessorResponseCode _0580 = new("0580");

    /// <summary>
    /// BAD_RESPONSE_REVERSAL_REQUIRED.
    /// </summary>
    public static readonly ProcessorResponseCode _0800 = new("0800");

    /// <summary>
    /// CRYPTOGRAPHIC_FAILURE.
    /// </summary>
    public static readonly ProcessorResponseCode _0880 = new("0880");

    /// <summary>
    /// UNACCEPTABLE_PIN.
    /// </summary>
    public static readonly ProcessorResponseCode _0890 = new("0890");

    /// <summary>
    /// SYSTEM_MALFUNCTION.
    /// </summary>
    public static readonly ProcessorResponseCode _0960 = new("0960");

    /// <summary>
    /// CANCELLED_PAYMENT.
    /// </summary>
    public static readonly ProcessorResponseCode _0R00 = new("0R00");

    /// <summary>
    /// PARTIAL_AUTHORIZATION.
    /// </summary>
    public static readonly ProcessorResponseCode _1000 = new("1000");

    /// <summary>
    /// ISSUER_REJECTED.
    /// </summary>
    public static readonly ProcessorResponseCode _10Br = new("10BR");

    /// <summary>
    /// INVALID_DATA_FORMAT.
    /// </summary>
    public static readonly ProcessorResponseCode _1300 = new("1300");

    /// <summary>
    /// INVALID_AMOUNT.
    /// </summary>
    public static readonly ProcessorResponseCode _1310 = new("1310");

    /// <summary>
    /// INVALID_TRANSACTION_CARD_ISSUER_ACQUIRER.
    /// </summary>
    public static readonly ProcessorResponseCode _1312 = new("1312");

    /// <summary>
    /// INVALID_CAPTURE_DATE.
    /// </summary>
    public static readonly ProcessorResponseCode _1317 = new("1317");

    /// <summary>
    /// INVALID_CURRENCY_CODE.
    /// </summary>
    public static readonly ProcessorResponseCode _1320 = new("1320");

    /// <summary>
    /// INVALID_ACCOUNT.
    /// </summary>
    public static readonly ProcessorResponseCode _1330 = new("1330");

    /// <summary>
    /// INVALID_ACCOUNT_RECURRING.
    /// </summary>
    public static readonly ProcessorResponseCode _1335 = new("1335");

    /// <summary>
    /// INVALID_TERMINAL.
    /// </summary>
    public static readonly ProcessorResponseCode _1340 = new("1340");

    /// <summary>
    /// INVALID_MERCHANT.
    /// </summary>
    public static readonly ProcessorResponseCode _1350 = new("1350");

    /// <summary>
    /// RESTRICTED_OR_INACTIVE_ACCOUNT.
    /// </summary>
    public static readonly ProcessorResponseCode _1352 = new("1352");

    /// <summary>
    /// BAD_PROCESSING_CODE.
    /// </summary>
    public static readonly ProcessorResponseCode _1360 = new("1360");

    /// <summary>
    /// INVALID_MCC.
    /// </summary>
    public static readonly ProcessorResponseCode _1370 = new("1370");

    /// <summary>
    /// INVALID_EXPIRATION.
    /// </summary>
    public static readonly ProcessorResponseCode _1380 = new("1380");

    /// <summary>
    /// INVALID_CARD_VERIFICATION_VALUE.
    /// </summary>
    public static readonly ProcessorResponseCode _1382 = new("1382");

    /// <summary>
    /// INVALID_LIFE_CYCLE_OF_TRANSACTION.
    /// </summary>
    public static readonly ProcessorResponseCode _1384 = new("1384");

    /// <summary>
    /// INVALID_ORDER.
    /// </summary>
    public static readonly ProcessorResponseCode _1390 = new("1390");

    /// <summary>
    /// TRANSACTION_CANNOT_BE_COMPLETED.
    /// </summary>
    public static readonly ProcessorResponseCode _1393 = new("1393");

    /// <summary>
    /// GENERIC_DECLINE.
    /// </summary>
    public static readonly ProcessorResponseCode _5100 = new("5100");

    /// <summary>
    /// CVV2_FAILURE.
    /// </summary>
    public static readonly ProcessorResponseCode _5110 = new("5110");

    /// <summary>
    /// INSUFFICIENT_FUNDS.
    /// </summary>
    public static readonly ProcessorResponseCode _5120 = new("5120");

    /// <summary>
    /// INVALID_PIN.
    /// </summary>
    public static readonly ProcessorResponseCode _5130 = new("5130");

    /// <summary>
    /// DECLINED_PIN_TRY_EXCEEDED.
    /// </summary>
    public static readonly ProcessorResponseCode _5135 = new("5135");

    /// <summary>
    /// CARD_CLOSED.
    /// </summary>
    public static readonly ProcessorResponseCode _5140 = new("5140");

    /// <summary>
    /// PICKUP_CARD_SPECIAL_CONDITIONS. Try using another card. Do not retry the same card.
    /// </summary>
    public static readonly ProcessorResponseCode _5150 = new("5150");

    /// <summary>
    /// UNAUTHORIZED_USER.
    /// </summary>
    public static readonly ProcessorResponseCode _5160 = new("5160");

    /// <summary>
    /// AVS_FAILURE.
    /// </summary>
    public static readonly ProcessorResponseCode _5170 = new("5170");

    /// <summary>
    /// INVALID_OR_RESTRICTED_CARD. Try using another card. Do not retry the same card.
    /// </summary>
    public static readonly ProcessorResponseCode _5180 = new("5180");

    /// <summary>
    /// SOFT_AVS.
    /// </summary>
    public static readonly ProcessorResponseCode _5190 = new("5190");

    /// <summary>
    /// DUPLICATE_TRANSACTION.
    /// </summary>
    public static readonly ProcessorResponseCode _5200 = new("5200");

    /// <summary>
    /// INVALID_TRANSACTION.
    /// </summary>
    public static readonly ProcessorResponseCode _5210 = new("5210");

    /// <summary>
    /// EXPIRED_CARD.
    /// </summary>
    public static readonly ProcessorResponseCode _5400 = new("5400");

    /// <summary>
    /// INCORRECT_PIN_REENTER.
    /// </summary>
    public static readonly ProcessorResponseCode _5500 = new("5500");

    /// <summary>
    /// DECLINED_SCA_REQUIRED.
    /// </summary>
    public static readonly ProcessorResponseCode _5650 = new("5650");

    /// <summary>
    /// TRANSACTION_NOT_PERMITTED. Outside of scope of accepted business.
    /// </summary>
    public static readonly ProcessorResponseCode _5700 = new("5700");

    /// <summary>
    /// TX_ATTEMPTS_EXCEED_LIMIT.
    /// </summary>
    public static readonly ProcessorResponseCode _5710 = new("5710");

    /// <summary>
    /// REVERSAL_REJECTED.
    /// </summary>
    public static readonly ProcessorResponseCode _5800 = new("5800");

    /// <summary>
    /// INVALID_ISSUE.
    /// </summary>
    public static readonly ProcessorResponseCode _5900 = new("5900");

    /// <summary>
    /// ISSUER_NOT_AVAILABLE_NOT_RETRIABLE.
    /// </summary>
    public static readonly ProcessorResponseCode _5910 = new("5910");

    /// <summary>
    /// ISSUER_NOT_AVAILABLE_RETRIABLE.
    /// </summary>
    public static readonly ProcessorResponseCode _5920 = new("5920");

    /// <summary>
    /// CARD_NOT_ACTIVATED.
    /// </summary>
    public static readonly ProcessorResponseCode _5930 = new("5930");

    /// <summary>
    /// DECLINED_DUE_TO_UPDATED_ACCOUNT. External decline as an updated card has been issued.
    /// </summary>
    public static readonly ProcessorResponseCode _5950 = new("5950");

    /// <summary>
    /// ACCOUNT_NOT_ON_FILE.
    /// </summary>
    public static readonly ProcessorResponseCode _6300 = new("6300");

    /// <summary>
    /// APPROVED_NON_CAPTURE.
    /// </summary>
    public static readonly ProcessorResponseCode _7600 = new("7600");

    /// <summary>
    /// ERROR_3DS.
    /// </summary>
    public static readonly ProcessorResponseCode _7700 = new("7700");

    /// <summary>
    /// AUTHENTICATION_FAILED.
    /// </summary>
    public static readonly ProcessorResponseCode _7710 = new("7710");

    /// <summary>
    /// BIN_ERROR.
    /// </summary>
    public static readonly ProcessorResponseCode _7800 = new("7800");

    /// <summary>
    /// PIN_ERROR.
    /// </summary>
    public static readonly ProcessorResponseCode _7900 = new("7900");

    /// <summary>
    /// PROCESSOR_SYSTEM_ERROR.
    /// </summary>
    public static readonly ProcessorResponseCode _8000 = new("8000");

    /// <summary>
    /// HOST_KEY_ERROR.
    /// </summary>
    public static readonly ProcessorResponseCode _8010 = new("8010");

    /// <summary>
    /// CONFIGURATION_ERROR.
    /// </summary>
    public static readonly ProcessorResponseCode _8020 = new("8020");

    /// <summary>
    /// UNSUPPORTED_OPERATION.
    /// </summary>
    public static readonly ProcessorResponseCode _8030 = new("8030");

    /// <summary>
    /// FATAL_COMMUNICATION_ERROR.
    /// </summary>
    public static readonly ProcessorResponseCode _8100 = new("8100");

    /// <summary>
    /// RETRIABLE_COMMUNICATION_ERROR.
    /// </summary>
    public static readonly ProcessorResponseCode _8110 = new("8110");

    /// <summary>
    /// SYSTEM_UNAVAILABLE.
    /// </summary>
    public static readonly ProcessorResponseCode _8220 = new("8220");

    /// <summary>
    /// DECLINED_PLEASE_RETRY. Retry.
    /// </summary>
    public static readonly ProcessorResponseCode _9100 = new("9100");

    /// <summary>
    /// SUSPECTED_FRAUD. Try using another card. Do not retry the same card.
    /// </summary>
    public static readonly ProcessorResponseCode _9500 = new("9500");

    /// <summary>
    /// SECURITY_VIOLATION.
    /// </summary>
    public static readonly ProcessorResponseCode _9510 = new("9510");

    /// <summary>
    /// LOST_OR_STOLEN. Try using another card. Do not retry the same card.
    /// </summary>
    public static readonly ProcessorResponseCode _9520 = new("9520");

    /// <summary>
    /// HOLD_CALL_CENTER. The merchant must call the number on the back of the card. POS scenario.
    /// </summary>
    public static readonly ProcessorResponseCode _9530 = new("9530");

    /// <summary>
    /// REFUSED_CARD.
    /// </summary>
    public static readonly ProcessorResponseCode _9540 = new("9540");

    /// <summary>
    /// UNRECOGNIZED_RESPONSE_CODE.
    /// </summary>
    public static readonly ProcessorResponseCode _9600 = new("9600");

    /// <summary>
    /// CONTINGENCIES_NOT_RESOLVED.
    /// </summary>
    public static readonly ProcessorResponseCode Pcnr = new("PCNR");

    /// <summary>
    /// CVV_FAILURE.
    /// </summary>
    public static readonly ProcessorResponseCode Pcvv = new("PCVV");

    /// <summary>
    /// ACCOUNT_CLOSED. A previously open account is now closed
    /// </summary>
    public static readonly ProcessorResponseCode Pp06 = new("PP06");

    /// <summary>
    /// REATTEMPT_NOT_PERMITTED.
    /// </summary>
    public static readonly ProcessorResponseCode Pprn = new("PPRN");

    /// <summary>
    /// BILLING_ADDRESS.
    /// </summary>
    public static readonly ProcessorResponseCode Ppad = new("PPAD");

    /// <summary>
    /// ACCOUNT_BLOCKED_BY_ISSUER.
    /// </summary>
    public static readonly ProcessorResponseCode Ppab = new("PPAB");

    /// <summary>
    /// AMEX_DISABLED.
    /// </summary>
    public static readonly ProcessorResponseCode Ppae = new("PPAE");

    /// <summary>
    /// ADULT_GAMING_UNSUPPORTED.
    /// </summary>
    public static readonly ProcessorResponseCode Ppag = new("PPAG");

    /// <summary>
    /// AMOUNT_INCOMPATIBLE.
    /// </summary>
    public static readonly ProcessorResponseCode Ppai = new("PPAI");

    /// <summary>
    /// AUTH_RESULT.
    /// </summary>
    public static readonly ProcessorResponseCode Ppar = new("PPAR");

    /// <summary>
    /// MCC_CODE.
    /// </summary>
    public static readonly ProcessorResponseCode Ppau = new("PPAU");

    /// <summary>
    /// ARC_AVS.
    /// </summary>
    public static readonly ProcessorResponseCode Ppav = new("PPAV");

    /// <summary>
    /// AMOUNT_EXCEEDED.
    /// </summary>
    public static readonly ProcessorResponseCode Ppax = new("PPAX");

    /// <summary>
    /// BAD_GAMING.
    /// </summary>
    public static readonly ProcessorResponseCode Ppbg = new("PPBG");

    /// <summary>
    /// ARC_CVV.
    /// </summary>
    public static readonly ProcessorResponseCode Ppc2 = new("PPC2");

    /// <summary>
    /// CE_REGISTRATION_INCOMPLETE.
    /// </summary>
    public static readonly ProcessorResponseCode Ppce = new("PPCE");

    /// <summary>
    /// COUNTRY.
    /// </summary>
    public static readonly ProcessorResponseCode Ppco = new("PPCO");

    /// <summary>
    /// CREDIT_ERROR.
    /// </summary>
    public static readonly ProcessorResponseCode Ppcr = new("PPCR");

    /// <summary>
    /// CARD_TYPE_UNSUPPORTED.
    /// </summary>
    public static readonly ProcessorResponseCode Ppct = new("PPCT");

    /// <summary>
    /// CURRENCY_USED_INVALID.
    /// </summary>
    public static readonly ProcessorResponseCode Ppcu = new("PPCU");

    /// <summary>
    /// SECURE_ERROR_3DS.
    /// </summary>
    public static readonly ProcessorResponseCode Ppd3 = new("PPD3");

    /// <summary>
    /// DCC_UNSUPPORTED.
    /// </summary>
    public static readonly ProcessorResponseCode Ppdc = new("PPDC");

    /// <summary>
    /// DINERS_REJECT.
    /// </summary>
    public static readonly ProcessorResponseCode Ppdi = new("PPDI");

    /// <summary>
    /// AUTH_MESSAGE.
    /// </summary>
    public static readonly ProcessorResponseCode Ppdv = new("PPDV");

    /// <summary>
    /// DECLINE_THRESHOLD_BREACH.
    /// </summary>
    public static readonly ProcessorResponseCode Ppdt = new("PPDT");

    /// <summary>
    /// EXPIRED_FUNDING_INSTRUMENT.
    /// </summary>
    public static readonly ProcessorResponseCode Ppef = new("PPEF");

    /// <summary>
    /// EXCEEDS_FREQUENCY_LIMIT.
    /// </summary>
    public static readonly ProcessorResponseCode Ppel = new("PPEL");

    /// <summary>
    /// INTERNAL_SYSTEM_ERROR.
    /// </summary>
    public static readonly ProcessorResponseCode Pper = new("PPER");

    /// <summary>
    /// EXPIRY_DATE.
    /// </summary>
    public static readonly ProcessorResponseCode Ppex = new("PPEX");

    /// <summary>
    /// FUNDING_SOURCE_ALREADY_EXISTS.
    /// </summary>
    public static readonly ProcessorResponseCode Ppfe = new("PPFE");

    /// <summary>
    /// INVALID_FUNDING_INSTRUMENT.
    /// </summary>
    public static readonly ProcessorResponseCode Ppfi = new("PPFI");

    /// <summary>
    /// RESTRICTED_FUNDING_INSTRUMENT.
    /// </summary>
    public static readonly ProcessorResponseCode Ppfr = new("PPFR");

    /// <summary>
    /// FIELD_VALIDATION_FAILED.
    /// </summary>
    public static readonly ProcessorResponseCode Ppfv = new("PPFV");

    /// <summary>
    /// GAMING_REFUND_ERROR.
    /// </summary>
    public static readonly ProcessorResponseCode Ppgr = new("PPGR");

    /// <summary>
    /// H1_ERROR.
    /// </summary>
    public static readonly ProcessorResponseCode Pph1 = new("PPH1");

    /// <summary>
    /// IDEMPOTENCY_FAILURE.
    /// </summary>
    public static readonly ProcessorResponseCode Ppif = new("PPIF");

    /// <summary>
    /// INVALID_INPUT_FAILURE.
    /// </summary>
    public static readonly ProcessorResponseCode Ppii = new("PPII");

    /// <summary>
    /// ID_MISMATCH.
    /// </summary>
    public static readonly ProcessorResponseCode Ppim = new("PPIM");

    /// <summary>
    /// INVALID_TRACE_ID.
    /// </summary>
    public static readonly ProcessorResponseCode Ppit = new("PPIT");

    /// <summary>
    /// LATE_REVERSAL.
    /// </summary>
    public static readonly ProcessorResponseCode Pplr = new("PPLR");

    /// <summary>
    /// LARGE_STATUS_CODE.
    /// </summary>
    public static readonly ProcessorResponseCode Ppls = new("PPLS");

    /// <summary>
    /// MISSING_BUSINESS_RULE_OR_DATA.
    /// </summary>
    public static readonly ProcessorResponseCode Ppmb = new("PPMB");

    /// <summary>
    /// BLOCKED_Mastercard.
    /// </summary>
    public static readonly ProcessorResponseCode Ppmc = new("PPMC");

    /// <summary>
    /// DEPRECATED The PPMD value has been deprecated.
    /// </summary>
    public static readonly ProcessorResponseCode Ppmd = new("PPMD");

    /// <summary>
    /// NOT_SUPPORTED_NRC.
    /// </summary>
    public static readonly ProcessorResponseCode Ppnc = new("PPNC");

    /// <summary>
    /// EXCEEDS_NETWORK_FREQUENCY_LIMIT.
    /// </summary>
    public static readonly ProcessorResponseCode Ppnl = new("PPNL");

    /// <summary>
    /// NO_MID_FOUND.
    /// </summary>
    public static readonly ProcessorResponseCode Ppnm = new("PPNM");

    /// <summary>
    /// NETWORK_ERROR.
    /// </summary>
    public static readonly ProcessorResponseCode Ppnt = new("PPNT");

    /// <summary>
    /// NO_PHONE_FOR_DCC_TRANSACTION.
    /// </summary>
    public static readonly ProcessorResponseCode Ppph = new("PPPH");

    /// <summary>
    /// INVALID_PRODUCT.
    /// </summary>
    public static readonly ProcessorResponseCode Pppi = new("PPPI");

    /// <summary>
    /// INVALID_PAYMENT_METHOD.
    /// </summary>
    public static readonly ProcessorResponseCode Pppm = new("PPPM");

    /// <summary>
    /// QUASI_CASH_UNSUPPORTED.
    /// </summary>
    public static readonly ProcessorResponseCode Ppqc = new("PPQC");

    /// <summary>
    /// UNSUPPORT_REFUND_ON_PENDING_BC.
    /// </summary>
    public static readonly ProcessorResponseCode Ppre = new("PPRE");

    /// <summary>
    /// INVALID_PARENT_TRANSACTION_STATUS.
    /// </summary>
    public static readonly ProcessorResponseCode Pprf = new("PPRF");

    /// <summary>
    /// MERCHANT_NOT_REGISTERED.
    /// </summary>
    public static readonly ProcessorResponseCode Pprr = new("PPRR");

    /// <summary>
    /// BANKAUTH_ROW_MISMATCH.
    /// </summary>
    public static readonly ProcessorResponseCode Pps0 = new("PPS0");

    /// <summary>
    /// BANKAUTH_ROW_SETTLED.
    /// </summary>
    public static readonly ProcessorResponseCode Pps1 = new("PPS1");

    /// <summary>
    /// BANKAUTH_ROW_VOIDED.
    /// </summary>
    public static readonly ProcessorResponseCode Pps2 = new("PPS2");

    /// <summary>
    /// BANKAUTH_EXPIRED.
    /// </summary>
    public static readonly ProcessorResponseCode Pps3 = new("PPS3");

    /// <summary>
    /// CURRENCY_MISMATCH.
    /// </summary>
    public static readonly ProcessorResponseCode Pps4 = new("PPS4");

    /// <summary>
    /// CREDITCARD_MISMATCH.
    /// </summary>
    public static readonly ProcessorResponseCode Pps5 = new("PPS5");

    /// <summary>
    /// AMOUNT_MISMATCH.
    /// </summary>
    public static readonly ProcessorResponseCode Pps6 = new("PPS6");

    /// <summary>
    /// ARC_SCORE.
    /// </summary>
    public static readonly ProcessorResponseCode Ppsc = new("PPSC");

    /// <summary>
    /// STATUS_DESCRIPTION.
    /// </summary>
    public static readonly ProcessorResponseCode Ppsd = new("PPSD");

    /// <summary>
    /// AMEX_DENIED.
    /// </summary>
    public static readonly ProcessorResponseCode Ppse = new("PPSE");

    /// <summary>
    /// VERIFICATION_TOKEN_EXPIRED.
    /// </summary>
    public static readonly ProcessorResponseCode Ppte = new("PPTE");

    /// <summary>
    /// INVALID_TRACE_REFERENCE.
    /// </summary>
    public static readonly ProcessorResponseCode Pptf = new("PPTF");

    /// <summary>
    /// INVALID_TRANSACTION_ID.
    /// </summary>
    public static readonly ProcessorResponseCode Ppti = new("PPTI");

    /// <summary>
    /// VERIFICATION_TOKEN_REVOKED.
    /// </summary>
    public static readonly ProcessorResponseCode Pptr = new("PPTR");

    /// <summary>
    /// TRANSACTION_TYPE_UNSUPPORTED.
    /// </summary>
    public static readonly ProcessorResponseCode Pptt = new("PPTT");

    /// <summary>
    /// INVALID_VERIFICATION_TOKEN.
    /// </summary>
    public static readonly ProcessorResponseCode Pptv = new("PPTV");

    /// <summary>
    /// USER_NOT_AUTHORIZED.
    /// </summary>
    public static readonly ProcessorResponseCode Ppua = new("PPUA");

    /// <summary>
    /// CURRENCY_CODE_UNSUPPORTED.
    /// </summary>
    public static readonly ProcessorResponseCode Ppuc = new("PPUC");

    /// <summary>
    /// UNSUPPORT_ENTITY.
    /// </summary>
    public static readonly ProcessorResponseCode Ppue = new("PPUE");

    /// <summary>
    /// UNSUPPORT_INSTALLMENT.
    /// </summary>
    public static readonly ProcessorResponseCode Ppui = new("PPUI");

    /// <summary>
    /// UNSUPPORT_POS_FLAG.
    /// </summary>
    public static readonly ProcessorResponseCode Ppup = new("PPUP");

    /// <summary>
    /// UNSUPPORTED_REVERSAL.
    /// </summary>
    public static readonly ProcessorResponseCode Ppur = new("PPUR");

    /// <summary>
    /// VALIDATE_CURRENCY.
    /// </summary>
    public static readonly ProcessorResponseCode Ppvc = new("PPVC");

    /// <summary>
    /// VALIDATION_ERROR.
    /// </summary>
    public static readonly ProcessorResponseCode Ppve = new("PPVE");

    /// <summary>
    /// VIRTUAL_TERMINAL_UNSUPPORTED.
    /// </summary>
    public static readonly ProcessorResponseCode Ppvt = new("PPVT");

    public static ProcessorResponseCode FromValue(string value) => FromValueCore(value);
}

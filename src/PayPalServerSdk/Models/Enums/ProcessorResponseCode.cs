using System;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// Processor response code for the non-PayPal payment processor errors.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ProcessorResponseCode>))]
public sealed record ProcessorResponseCode : OpenStringEnum<ProcessorResponseCode>
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

    public TResult Match<TResult>(Func<TResult> on_0000,
        Func<TResult> on_00N7,
        Func<TResult> on_0100,
        Func<TResult> on_0390,
        Func<TResult> on_0500,
        Func<TResult> on_0580,
        Func<TResult> on_0800,
        Func<TResult> on_0880,
        Func<TResult> on_0890,
        Func<TResult> on_0960,
        Func<TResult> on_0R00,
        Func<TResult> on_1000,
        Func<TResult> on_10Br,
        Func<TResult> on_1300,
        Func<TResult> on_1310,
        Func<TResult> on_1312,
        Func<TResult> on_1317,
        Func<TResult> on_1320,
        Func<TResult> on_1330,
        Func<TResult> on_1335,
        Func<TResult> on_1340,
        Func<TResult> on_1350,
        Func<TResult> on_1352,
        Func<TResult> on_1360,
        Func<TResult> on_1370,
        Func<TResult> on_1380,
        Func<TResult> on_1382,
        Func<TResult> on_1384,
        Func<TResult> on_1390,
        Func<TResult> on_1393,
        Func<TResult> on_5100,
        Func<TResult> on_5110,
        Func<TResult> on_5120,
        Func<TResult> on_5130,
        Func<TResult> on_5135,
        Func<TResult> on_5140,
        Func<TResult> on_5150,
        Func<TResult> on_5160,
        Func<TResult> on_5170,
        Func<TResult> on_5180,
        Func<TResult> on_5190,
        Func<TResult> on_5200,
        Func<TResult> on_5210,
        Func<TResult> on_5400,
        Func<TResult> on_5500,
        Func<TResult> on_5650,
        Func<TResult> on_5700,
        Func<TResult> on_5710,
        Func<TResult> on_5800,
        Func<TResult> on_5900,
        Func<TResult> on_5910,
        Func<TResult> on_5920,
        Func<TResult> on_5930,
        Func<TResult> on_5950,
        Func<TResult> on_6300,
        Func<TResult> on_7600,
        Func<TResult> on_7700,
        Func<TResult> on_7710,
        Func<TResult> on_7800,
        Func<TResult> on_7900,
        Func<TResult> on_8000,
        Func<TResult> on_8010,
        Func<TResult> on_8020,
        Func<TResult> on_8030,
        Func<TResult> on_8100,
        Func<TResult> on_8110,
        Func<TResult> on_8220,
        Func<TResult> on_9100,
        Func<TResult> on_9500,
        Func<TResult> on_9510,
        Func<TResult> on_9520,
        Func<TResult> on_9530,
        Func<TResult> on_9540,
        Func<TResult> on_9600,
        Func<TResult> onPcnr,
        Func<TResult> onPcvv,
        Func<TResult> onPp06,
        Func<TResult> onPprn,
        Func<TResult> onPpad,
        Func<TResult> onPpab,
        Func<TResult> onPpae,
        Func<TResult> onPpag,
        Func<TResult> onPpai,
        Func<TResult> onPpar,
        Func<TResult> onPpau,
        Func<TResult> onPpav,
        Func<TResult> onPpax,
        Func<TResult> onPpbg,
        Func<TResult> onPpc2,
        Func<TResult> onPpce,
        Func<TResult> onPpco,
        Func<TResult> onPpcr,
        Func<TResult> onPpct,
        Func<TResult> onPpcu,
        Func<TResult> onPpd3,
        Func<TResult> onPpdc,
        Func<TResult> onPpdi,
        Func<TResult> onPpdv,
        Func<TResult> onPpdt,
        Func<TResult> onPpef,
        Func<TResult> onPpel,
        Func<TResult> onPper,
        Func<TResult> onPpex,
        Func<TResult> onPpfe,
        Func<TResult> onPpfi,
        Func<TResult> onPpfr,
        Func<TResult> onPpfv,
        Func<TResult> onPpgr,
        Func<TResult> onPph1,
        Func<TResult> onPpif,
        Func<TResult> onPpii,
        Func<TResult> onPpim,
        Func<TResult> onPpit,
        Func<TResult> onPplr,
        Func<TResult> onPpls,
        Func<TResult> onPpmb,
        Func<TResult> onPpmc,
        Func<TResult> onPpmd,
        Func<TResult> onPpnc,
        Func<TResult> onPpnl,
        Func<TResult> onPpnm,
        Func<TResult> onPpnt,
        Func<TResult> onPpph,
        Func<TResult> onPppi,
        Func<TResult> onPppm,
        Func<TResult> onPpqc,
        Func<TResult> onPpre,
        Func<TResult> onPprf,
        Func<TResult> onPprr,
        Func<TResult> onPps0,
        Func<TResult> onPps1,
        Func<TResult> onPps2,
        Func<TResult> onPps3,
        Func<TResult> onPps4,
        Func<TResult> onPps5,
        Func<TResult> onPps6,
        Func<TResult> onPpsc,
        Func<TResult> onPpsd,
        Func<TResult> onPpse,
        Func<TResult> onPpte,
        Func<TResult> onPptf,
        Func<TResult> onPpti,
        Func<TResult> onPptr,
        Func<TResult> onPptt,
        Func<TResult> onPptv,
        Func<TResult> onPpua,
        Func<TResult> onPpuc,
        Func<TResult> onPpue,
        Func<TResult> onPpui,
        Func<TResult> onPpup,
        Func<TResult> onPpur,
        Func<TResult> onPpvc,
        Func<TResult> onPpve,
        Func<TResult> onPpvt,
        Func<string, TResult> otherwise) =>
        this switch
        {
            _ when this == _0000 => on_0000(),
            _ when this == _00N7 => on_00N7(),
            _ when this == _0100 => on_0100(),
            _ when this == _0390 => on_0390(),
            _ when this == _0500 => on_0500(),
            _ when this == _0580 => on_0580(),
            _ when this == _0800 => on_0800(),
            _ when this == _0880 => on_0880(),
            _ when this == _0890 => on_0890(),
            _ when this == _0960 => on_0960(),
            _ when this == _0R00 => on_0R00(),
            _ when this == _1000 => on_1000(),
            _ when this == _10Br => on_10Br(),
            _ when this == _1300 => on_1300(),
            _ when this == _1310 => on_1310(),
            _ when this == _1312 => on_1312(),
            _ when this == _1317 => on_1317(),
            _ when this == _1320 => on_1320(),
            _ when this == _1330 => on_1330(),
            _ when this == _1335 => on_1335(),
            _ when this == _1340 => on_1340(),
            _ when this == _1350 => on_1350(),
            _ when this == _1352 => on_1352(),
            _ when this == _1360 => on_1360(),
            _ when this == _1370 => on_1370(),
            _ when this == _1380 => on_1380(),
            _ when this == _1382 => on_1382(),
            _ when this == _1384 => on_1384(),
            _ when this == _1390 => on_1390(),
            _ when this == _1393 => on_1393(),
            _ when this == _5100 => on_5100(),
            _ when this == _5110 => on_5110(),
            _ when this == _5120 => on_5120(),
            _ when this == _5130 => on_5130(),
            _ when this == _5135 => on_5135(),
            _ when this == _5140 => on_5140(),
            _ when this == _5150 => on_5150(),
            _ when this == _5160 => on_5160(),
            _ when this == _5170 => on_5170(),
            _ when this == _5180 => on_5180(),
            _ when this == _5190 => on_5190(),
            _ when this == _5200 => on_5200(),
            _ when this == _5210 => on_5210(),
            _ when this == _5400 => on_5400(),
            _ when this == _5500 => on_5500(),
            _ when this == _5650 => on_5650(),
            _ when this == _5700 => on_5700(),
            _ when this == _5710 => on_5710(),
            _ when this == _5800 => on_5800(),
            _ when this == _5900 => on_5900(),
            _ when this == _5910 => on_5910(),
            _ when this == _5920 => on_5920(),
            _ when this == _5930 => on_5930(),
            _ when this == _5950 => on_5950(),
            _ when this == _6300 => on_6300(),
            _ when this == _7600 => on_7600(),
            _ when this == _7700 => on_7700(),
            _ when this == _7710 => on_7710(),
            _ when this == _7800 => on_7800(),
            _ when this == _7900 => on_7900(),
            _ when this == _8000 => on_8000(),
            _ when this == _8010 => on_8010(),
            _ when this == _8020 => on_8020(),
            _ when this == _8030 => on_8030(),
            _ when this == _8100 => on_8100(),
            _ when this == _8110 => on_8110(),
            _ when this == _8220 => on_8220(),
            _ when this == _9100 => on_9100(),
            _ when this == _9500 => on_9500(),
            _ when this == _9510 => on_9510(),
            _ when this == _9520 => on_9520(),
            _ when this == _9530 => on_9530(),
            _ when this == _9540 => on_9540(),
            _ when this == _9600 => on_9600(),
            _ when this == Pcnr => onPcnr(),
            _ when this == Pcvv => onPcvv(),
            _ when this == Pp06 => onPp06(),
            _ when this == Pprn => onPprn(),
            _ when this == Ppad => onPpad(),
            _ when this == Ppab => onPpab(),
            _ when this == Ppae => onPpae(),
            _ when this == Ppag => onPpag(),
            _ when this == Ppai => onPpai(),
            _ when this == Ppar => onPpar(),
            _ when this == Ppau => onPpau(),
            _ when this == Ppav => onPpav(),
            _ when this == Ppax => onPpax(),
            _ when this == Ppbg => onPpbg(),
            _ when this == Ppc2 => onPpc2(),
            _ when this == Ppce => onPpce(),
            _ when this == Ppco => onPpco(),
            _ when this == Ppcr => onPpcr(),
            _ when this == Ppct => onPpct(),
            _ when this == Ppcu => onPpcu(),
            _ when this == Ppd3 => onPpd3(),
            _ when this == Ppdc => onPpdc(),
            _ when this == Ppdi => onPpdi(),
            _ when this == Ppdv => onPpdv(),
            _ when this == Ppdt => onPpdt(),
            _ when this == Ppef => onPpef(),
            _ when this == Ppel => onPpel(),
            _ when this == Pper => onPper(),
            _ when this == Ppex => onPpex(),
            _ when this == Ppfe => onPpfe(),
            _ when this == Ppfi => onPpfi(),
            _ when this == Ppfr => onPpfr(),
            _ when this == Ppfv => onPpfv(),
            _ when this == Ppgr => onPpgr(),
            _ when this == Pph1 => onPph1(),
            _ when this == Ppif => onPpif(),
            _ when this == Ppii => onPpii(),
            _ when this == Ppim => onPpim(),
            _ when this == Ppit => onPpit(),
            _ when this == Pplr => onPplr(),
            _ when this == Ppls => onPpls(),
            _ when this == Ppmb => onPpmb(),
            _ when this == Ppmc => onPpmc(),
            _ when this == Ppmd => onPpmd(),
            _ when this == Ppnc => onPpnc(),
            _ when this == Ppnl => onPpnl(),
            _ when this == Ppnm => onPpnm(),
            _ when this == Ppnt => onPpnt(),
            _ when this == Ppph => onPpph(),
            _ when this == Pppi => onPppi(),
            _ when this == Pppm => onPppm(),
            _ when this == Ppqc => onPpqc(),
            _ when this == Ppre => onPpre(),
            _ when this == Pprf => onPprf(),
            _ when this == Pprr => onPprr(),
            _ when this == Pps0 => onPps0(),
            _ when this == Pps1 => onPps1(),
            _ when this == Pps2 => onPps2(),
            _ when this == Pps3 => onPps3(),
            _ when this == Pps4 => onPps4(),
            _ when this == Pps5 => onPps5(),
            _ when this == Pps6 => onPps6(),
            _ when this == Ppsc => onPpsc(),
            _ when this == Ppsd => onPpsd(),
            _ when this == Ppse => onPpse(),
            _ when this == Ppte => onPpte(),
            _ when this == Pptf => onPptf(),
            _ when this == Ppti => onPpti(),
            _ when this == Pptr => onPptr(),
            _ when this == Pptt => onPptt(),
            _ when this == Pptv => onPptv(),
            _ when this == Ppua => onPpua(),
            _ when this == Ppuc => onPpuc(),
            _ when this == Ppue => onPpue(),
            _ when this == Ppui => onPpui(),
            _ when this == Ppup => onPpup(),
            _ when this == Ppur => onPpur(),
            _ when this == Ppvc => onPpvc(),
            _ when this == Ppve => onPpve(),
            _ when this == Ppvt => onPpvt(),
            _ => otherwise(Value)
        };

    public void Match(Action on_0000,
        Action on_00N7,
        Action on_0100,
        Action on_0390,
        Action on_0500,
        Action on_0580,
        Action on_0800,
        Action on_0880,
        Action on_0890,
        Action on_0960,
        Action on_0R00,
        Action on_1000,
        Action on_10Br,
        Action on_1300,
        Action on_1310,
        Action on_1312,
        Action on_1317,
        Action on_1320,
        Action on_1330,
        Action on_1335,
        Action on_1340,
        Action on_1350,
        Action on_1352,
        Action on_1360,
        Action on_1370,
        Action on_1380,
        Action on_1382,
        Action on_1384,
        Action on_1390,
        Action on_1393,
        Action on_5100,
        Action on_5110,
        Action on_5120,
        Action on_5130,
        Action on_5135,
        Action on_5140,
        Action on_5150,
        Action on_5160,
        Action on_5170,
        Action on_5180,
        Action on_5190,
        Action on_5200,
        Action on_5210,
        Action on_5400,
        Action on_5500,
        Action on_5650,
        Action on_5700,
        Action on_5710,
        Action on_5800,
        Action on_5900,
        Action on_5910,
        Action on_5920,
        Action on_5930,
        Action on_5950,
        Action on_6300,
        Action on_7600,
        Action on_7700,
        Action on_7710,
        Action on_7800,
        Action on_7900,
        Action on_8000,
        Action on_8010,
        Action on_8020,
        Action on_8030,
        Action on_8100,
        Action on_8110,
        Action on_8220,
        Action on_9100,
        Action on_9500,
        Action on_9510,
        Action on_9520,
        Action on_9530,
        Action on_9540,
        Action on_9600,
        Action onPcnr,
        Action onPcvv,
        Action onPp06,
        Action onPprn,
        Action onPpad,
        Action onPpab,
        Action onPpae,
        Action onPpag,
        Action onPpai,
        Action onPpar,
        Action onPpau,
        Action onPpav,
        Action onPpax,
        Action onPpbg,
        Action onPpc2,
        Action onPpce,
        Action onPpco,
        Action onPpcr,
        Action onPpct,
        Action onPpcu,
        Action onPpd3,
        Action onPpdc,
        Action onPpdi,
        Action onPpdv,
        Action onPpdt,
        Action onPpef,
        Action onPpel,
        Action onPper,
        Action onPpex,
        Action onPpfe,
        Action onPpfi,
        Action onPpfr,
        Action onPpfv,
        Action onPpgr,
        Action onPph1,
        Action onPpif,
        Action onPpii,
        Action onPpim,
        Action onPpit,
        Action onPplr,
        Action onPpls,
        Action onPpmb,
        Action onPpmc,
        Action onPpmd,
        Action onPpnc,
        Action onPpnl,
        Action onPpnm,
        Action onPpnt,
        Action onPpph,
        Action onPppi,
        Action onPppm,
        Action onPpqc,
        Action onPpre,
        Action onPprf,
        Action onPprr,
        Action onPps0,
        Action onPps1,
        Action onPps2,
        Action onPps3,
        Action onPps4,
        Action onPps5,
        Action onPps6,
        Action onPpsc,
        Action onPpsd,
        Action onPpse,
        Action onPpte,
        Action onPptf,
        Action onPpti,
        Action onPptr,
        Action onPptt,
        Action onPptv,
        Action onPpua,
        Action onPpuc,
        Action onPpue,
        Action onPpui,
        Action onPpup,
        Action onPpur,
        Action onPpvc,
        Action onPpve,
        Action onPpvt,
        Action<string> otherwise)
    {
        if (this == _0000) on_0000();
        else if (this == _00N7) on_00N7();
        else if (this == _0100) on_0100();
        else if (this == _0390) on_0390();
        else if (this == _0500) on_0500();
        else if (this == _0580) on_0580();
        else if (this == _0800) on_0800();
        else if (this == _0880) on_0880();
        else if (this == _0890) on_0890();
        else if (this == _0960) on_0960();
        else if (this == _0R00) on_0R00();
        else if (this == _1000) on_1000();
        else if (this == _10Br) on_10Br();
        else if (this == _1300) on_1300();
        else if (this == _1310) on_1310();
        else if (this == _1312) on_1312();
        else if (this == _1317) on_1317();
        else if (this == _1320) on_1320();
        else if (this == _1330) on_1330();
        else if (this == _1335) on_1335();
        else if (this == _1340) on_1340();
        else if (this == _1350) on_1350();
        else if (this == _1352) on_1352();
        else if (this == _1360) on_1360();
        else if (this == _1370) on_1370();
        else if (this == _1380) on_1380();
        else if (this == _1382) on_1382();
        else if (this == _1384) on_1384();
        else if (this == _1390) on_1390();
        else if (this == _1393) on_1393();
        else if (this == _5100) on_5100();
        else if (this == _5110) on_5110();
        else if (this == _5120) on_5120();
        else if (this == _5130) on_5130();
        else if (this == _5135) on_5135();
        else if (this == _5140) on_5140();
        else if (this == _5150) on_5150();
        else if (this == _5160) on_5160();
        else if (this == _5170) on_5170();
        else if (this == _5180) on_5180();
        else if (this == _5190) on_5190();
        else if (this == _5200) on_5200();
        else if (this == _5210) on_5210();
        else if (this == _5400) on_5400();
        else if (this == _5500) on_5500();
        else if (this == _5650) on_5650();
        else if (this == _5700) on_5700();
        else if (this == _5710) on_5710();
        else if (this == _5800) on_5800();
        else if (this == _5900) on_5900();
        else if (this == _5910) on_5910();
        else if (this == _5920) on_5920();
        else if (this == _5930) on_5930();
        else if (this == _5950) on_5950();
        else if (this == _6300) on_6300();
        else if (this == _7600) on_7600();
        else if (this == _7700) on_7700();
        else if (this == _7710) on_7710();
        else if (this == _7800) on_7800();
        else if (this == _7900) on_7900();
        else if (this == _8000) on_8000();
        else if (this == _8010) on_8010();
        else if (this == _8020) on_8020();
        else if (this == _8030) on_8030();
        else if (this == _8100) on_8100();
        else if (this == _8110) on_8110();
        else if (this == _8220) on_8220();
        else if (this == _9100) on_9100();
        else if (this == _9500) on_9500();
        else if (this == _9510) on_9510();
        else if (this == _9520) on_9520();
        else if (this == _9530) on_9530();
        else if (this == _9540) on_9540();
        else if (this == _9600) on_9600();
        else if (this == Pcnr) onPcnr();
        else if (this == Pcvv) onPcvv();
        else if (this == Pp06) onPp06();
        else if (this == Pprn) onPprn();
        else if (this == Ppad) onPpad();
        else if (this == Ppab) onPpab();
        else if (this == Ppae) onPpae();
        else if (this == Ppag) onPpag();
        else if (this == Ppai) onPpai();
        else if (this == Ppar) onPpar();
        else if (this == Ppau) onPpau();
        else if (this == Ppav) onPpav();
        else if (this == Ppax) onPpax();
        else if (this == Ppbg) onPpbg();
        else if (this == Ppc2) onPpc2();
        else if (this == Ppce) onPpce();
        else if (this == Ppco) onPpco();
        else if (this == Ppcr) onPpcr();
        else if (this == Ppct) onPpct();
        else if (this == Ppcu) onPpcu();
        else if (this == Ppd3) onPpd3();
        else if (this == Ppdc) onPpdc();
        else if (this == Ppdi) onPpdi();
        else if (this == Ppdv) onPpdv();
        else if (this == Ppdt) onPpdt();
        else if (this == Ppef) onPpef();
        else if (this == Ppel) onPpel();
        else if (this == Pper) onPper();
        else if (this == Ppex) onPpex();
        else if (this == Ppfe) onPpfe();
        else if (this == Ppfi) onPpfi();
        else if (this == Ppfr) onPpfr();
        else if (this == Ppfv) onPpfv();
        else if (this == Ppgr) onPpgr();
        else if (this == Pph1) onPph1();
        else if (this == Ppif) onPpif();
        else if (this == Ppii) onPpii();
        else if (this == Ppim) onPpim();
        else if (this == Ppit) onPpit();
        else if (this == Pplr) onPplr();
        else if (this == Ppls) onPpls();
        else if (this == Ppmb) onPpmb();
        else if (this == Ppmc) onPpmc();
        else if (this == Ppmd) onPpmd();
        else if (this == Ppnc) onPpnc();
        else if (this == Ppnl) onPpnl();
        else if (this == Ppnm) onPpnm();
        else if (this == Ppnt) onPpnt();
        else if (this == Ppph) onPpph();
        else if (this == Pppi) onPppi();
        else if (this == Pppm) onPppm();
        else if (this == Ppqc) onPpqc();
        else if (this == Ppre) onPpre();
        else if (this == Pprf) onPprf();
        else if (this == Pprr) onPprr();
        else if (this == Pps0) onPps0();
        else if (this == Pps1) onPps1();
        else if (this == Pps2) onPps2();
        else if (this == Pps3) onPps3();
        else if (this == Pps4) onPps4();
        else if (this == Pps5) onPps5();
        else if (this == Pps6) onPps6();
        else if (this == Ppsc) onPpsc();
        else if (this == Ppsd) onPpsd();
        else if (this == Ppse) onPpse();
        else if (this == Ppte) onPpte();
        else if (this == Pptf) onPptf();
        else if (this == Ppti) onPpti();
        else if (this == Pptr) onPptr();
        else if (this == Pptt) onPptt();
        else if (this == Pptv) onPptv();
        else if (this == Ppua) onPpua();
        else if (this == Ppuc) onPpuc();
        else if (this == Ppue) onPpue();
        else if (this == Ppui) onPpui();
        else if (this == Ppup) onPpup();
        else if (this == Ppur) onPpur();
        else if (this == Ppvc) onPpvc();
        else if (this == Ppve) onPpve();
        else if (this == Ppvt) onPpvt();
        else otherwise(Value);
    }
}

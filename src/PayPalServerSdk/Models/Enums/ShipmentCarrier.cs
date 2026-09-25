using System;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The carrier for the shipment. Some carriers have a global version as well as local subsidiaries. The subsidiaries are repeated over many countries and might also have an entry in the global list. Choose the carrier for your country. If the carrier is not available for your country, choose the global version of the carrier. If your carrier name is not in the list, set <c>carrier</c> to <c>OTHER</c> and set carrier name in <c>carrier_name_other</c>. For allowed values, see Carriers.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ShipmentCarrier>))]
public sealed record ShipmentCarrier : OpenStringEnum<ShipmentCarrier>
{
    private ShipmentCarrier(string value) : base(value)
    {
    }

    /// <summary>
    /// DPD Russia.
    /// </summary>
    public static readonly ShipmentCarrier DpdRu = new("DPD_RU");

    /// <summary>
    /// Bulgarian Posts.
    /// </summary>
    public static readonly ShipmentCarrier BgBulgarianPost = new("BG_BULGARIAN_POST");

    /// <summary>
    /// Koreapost (www.koreapost.go.kr).
    /// </summary>
    public static readonly ShipmentCarrier KrKoreaPost = new("KR_KOREA_POST");

    /// <summary>
    /// Courier IT.
    /// </summary>
    public static readonly ShipmentCarrier ZaCourierit = new("ZA_COURIERIT");

    /// <summary>
    /// DPD France (formerly exapaq).
    /// </summary>
    public static readonly ShipmentCarrier FrExapaq = new("FR_EXAPAQ");

    /// <summary>
    /// Emirates Post.
    /// </summary>
    public static readonly ShipmentCarrier AreEmiratesPost = new("ARE_EMIRATES_POST");

    /// <summary>
    /// GAC.
    /// </summary>
    public static readonly ShipmentCarrier Gac = new("GAC");

    /// <summary>
    /// Geis CZ.
    /// </summary>
    public static readonly ShipmentCarrier Geis = new("GEIS");

    /// <summary>
    /// SF Express.
    /// </summary>
    public static readonly ShipmentCarrier SfEx = new("SF_EX");

    /// <summary>
    /// Pago Logistics.
    /// </summary>
    public static readonly ShipmentCarrier Pago = new("PAGO");

    /// <summary>
    /// MyHermes UK.
    /// </summary>
    public static readonly ShipmentCarrier Myhermes = new("MYHERMES");

    /// <summary>
    /// Diamond Eurogistics Limited.
    /// </summary>
    public static readonly ShipmentCarrier DiamondEurogistics = new("DIAMOND_EUROGISTICS");

    /// <summary>
    /// Corporate Couriers.
    /// </summary>
    public static readonly ShipmentCarrier CorporatecouriersWebhook = new("CORPORATECOURIERS_WEBHOOK");

    /// <summary>
    /// Bond courier.
    /// </summary>
    public static readonly ShipmentCarrier Bond = new("BOND");

    /// <summary>
    /// Omni Parcel.
    /// </summary>
    public static readonly ShipmentCarrier Omniparcel = new("OMNIPARCEL");

    /// <summary>
    /// Slovenska pošta.
    /// </summary>
    public static readonly ShipmentCarrier SkPosta = new("SK_POSTA");

    /// <summary>
    /// purolator.
    /// </summary>
    public static readonly ShipmentCarrier Purolator = new("PUROLATOR");

    /// <summary>
    /// Mena 360 (Fetchr).
    /// </summary>
    public static readonly ShipmentCarrier FetchrWebhook = new("FETCHR_WEBHOOK");

    /// <summary>
    /// TDG – The Delivery Group.
    /// </summary>
    public static readonly ShipmentCarrier Thedeliverygroup = new("THEDELIVERYGROUP");

    /// <summary>
    /// Cello Square.
    /// </summary>
    public static readonly ShipmentCarrier CelloSquare = new("CELLO_SQUARE");

    /// <summary>
    /// TONDA GLOBAL.
    /// </summary>
    public static readonly ShipmentCarrier Tarrive = new("TARRIVE");

    /// <summary>
    /// MDS Collivery Pty (Ltd).
    /// </summary>
    public static readonly ShipmentCarrier Collivery = new("COLLIVERY");

    /// <summary>
    /// Mainfreight.
    /// </summary>
    public static readonly ShipmentCarrier Mainfreight = new("MAINFREIGHT");

    /// <summary>
    /// First Flight Couriers.
    /// </summary>
    public static readonly ShipmentCarrier IndFirstflight = new("IND_FIRSTFLIGHT");

    /// <summary>
    /// ACS Worldwide Express.
    /// </summary>
    public static readonly ShipmentCarrier Acsworldwide = new("ACSWORLDWIDE");

    /// <summary>
    /// Amstan Logistics.
    /// </summary>
    public static readonly ShipmentCarrier Amstan = new("AMSTAN");

    /// <summary>
    /// OkayParcel.
    /// </summary>
    public static readonly ShipmentCarrier Okayparcel = new("OKAYPARCEL");

    /// <summary>
    /// Envialia Reference.
    /// </summary>
    public static readonly ShipmentCarrier EnvialiaReference = new("ENVIALIA_REFERENCE");

    /// <summary>
    /// Seur Spain.
    /// </summary>
    public static readonly ShipmentCarrier SeurEs = new("SEUR_ES");

    /// <summary>
    /// Continental.
    /// </summary>
    public static readonly ShipmentCarrier Continental = new("CONTINENTAL");

    /// <summary>
    /// FDSEXPRESS.
    /// </summary>
    public static readonly ShipmentCarrier Fdsexpress = new("FDSEXPRESS");

    /// <summary>
    /// Swiship UK.
    /// </summary>
    public static readonly ShipmentCarrier AmazonFbaSwiship = new("AMAZON_FBA_SWISHIP");

    /// <summary>
    /// Wyngs.
    /// </summary>
    public static readonly ShipmentCarrier Wyngs = new("WYNGS");

    /// <summary>
    /// DHL Active Tracing.
    /// </summary>
    public static readonly ShipmentCarrier DhlActiveTracing = new("DHL_ACTIVE_TRACING");

    /// <summary>
    /// Zyllem.
    /// </summary>
    public static readonly ShipmentCarrier Zyllem = new("ZYLLEM");

    /// <summary>
    /// Ruston.
    /// </summary>
    public static readonly ShipmentCarrier Ruston = new("RUSTON");

    /// <summary>
    /// Xpost.ph.
    /// </summary>
    public static readonly ShipmentCarrier Xpost = new("XPOST");

    /// <summary>
    /// correos Express (www.correos.es).
    /// </summary>
    public static readonly ShipmentCarrier CorreosEs = new("CORREOS_ES");

    /// <summary>
    /// DHL France (www.dhl.com).
    /// </summary>
    public static readonly ShipmentCarrier DhlFr = new("DHL_FR");

    /// <summary>
    /// Pan-Asia International.
    /// </summary>
    public static readonly ShipmentCarrier PanAsia = new("PAN_ASIA");

    /// <summary>
    /// BRT couriers Italy (www.brt.it).
    /// </summary>
    public static readonly ShipmentCarrier BrtIt = new("BRT_IT");

    /// <summary>
    /// SRE Korea (www.srekorea.co.kr).
    /// </summary>
    public static readonly ShipmentCarrier SreKorea = new("SRE_KOREA");

    /// <summary>
    /// Spee-Dee Delivery.
    /// </summary>
    public static readonly ShipmentCarrier Speedee = new("SPEEDEE");

    /// <summary>
    /// TNT UK Limited (www.tnt.com).
    /// </summary>
    public static readonly ShipmentCarrier TntUk = new("TNT_UK");

    /// <summary>
    /// Venipak.
    /// </summary>
    public static readonly ShipmentCarrier Venipak = new("VENIPAK");

    /// <summary>
    /// SHREE NANDAN COURIER.
    /// </summary>
    public static readonly ShipmentCarrier Shreenandancourier = new("SHREENANDANCOURIER");

    /// <summary>
    /// Croshot.
    /// </summary>
    public static readonly ShipmentCarrier Croshot = new("CROSHOT");

    /// <summary>
    /// NIpost (www.nipost.gov.ng).
    /// </summary>
    public static readonly ShipmentCarrier NipostNg = new("NIPOST_NG");

    /// <summary>
    /// ePost Global.
    /// </summary>
    public static readonly ShipmentCarrier EpstGlbl = new("EPST_GLBL");

    /// <summary>
    /// Newgistics.
    /// </summary>
    public static readonly ShipmentCarrier Newgistics = new("NEWGISTICS");

    /// <summary>
    /// Post of Slovenia.
    /// </summary>
    public static readonly ShipmentCarrier PostSlovenia = new("POST_SLOVENIA");

    /// <summary>
    /// Jersey Post.
    /// </summary>
    public static readonly ShipmentCarrier JerseyPost = new("JERSEY_POST");

    /// <summary>
    /// Bombino Express Pvt.
    /// </summary>
    public static readonly ShipmentCarrier Bombinoexp = new("BOMBINOEXP");

    /// <summary>
    /// WMG Delivery.
    /// </summary>
    public static readonly ShipmentCarrier Wmg = new("WMG");

    /// <summary>
    /// XQ Express.
    /// </summary>
    public static readonly ShipmentCarrier XqExpress = new("XQ_EXPRESS");

    /// <summary>
    /// Furdeco.
    /// </summary>
    public static readonly ShipmentCarrier Furdeco = new("FURDECO");

    /// <summary>
    /// LHT Express.
    /// </summary>
    public static readonly ShipmentCarrier LhtExpress = new("LHT_EXPRESS");

    /// <summary>
    /// South African Post Office.
    /// </summary>
    public static readonly ShipmentCarrier SouthAfricanPostOffice = new("SOUTH_AFRICAN_POST_OFFICE");

    /// <summary>
    /// SPOTON Logistics Pvt Ltd.
    /// </summary>
    public static readonly ShipmentCarrier Spoton = new("SPOTON");

    /// <summary>
    /// Dimerco Express Group.
    /// </summary>
    public static readonly ShipmentCarrier Dimerco = new("DIMERCO");

    /// <summary>
    /// cyprus post.
    /// </summary>
    public static readonly ShipmentCarrier CyprusPostCyp = new("CYPRUS_POST_CYP");

    /// <summary>
    /// AB Custom Group.
    /// </summary>
    public static readonly ShipmentCarrier Abcustom = new("ABCUSTOM");

    /// <summary>
    /// deliverE.
    /// </summary>
    public static readonly ShipmentCarrier IndDelivree = new("IND_DELIVREE");

    /// <summary>
    /// Best Express.
    /// </summary>
    public static readonly ShipmentCarrier CnBestexpress = new("CN_BESTEXPRESS");

    /// <summary>
    /// DX (SFTP).
    /// </summary>
    public static readonly ShipmentCarrier DxSftp = new("DX_SFTP");

    /// <summary>
    /// PICK UPP.
    /// </summary>
    public static readonly ShipmentCarrier PickuppMys = new("PICKUPP_MYS");

    /// <summary>
    /// FMX.
    /// </summary>
    public static readonly ShipmentCarrier Fmx = new("FMX");

    /// <summary>
    /// Hellmann Worldwide Logistics.
    /// </summary>
    public static readonly ShipmentCarrier Hellmann = new("HELLMANN");

    /// <summary>
    /// Ship It Asia.
    /// </summary>
    public static readonly ShipmentCarrier ShipItAsia = new("SHIP_IT_ASIA");

    /// <summary>
    /// Kerry eCommerce.
    /// </summary>
    public static readonly ShipmentCarrier KerryEcommerce = new("KERRY_ECOMMERCE");

    /// <summary>
    /// Frete Rapido.
    /// </summary>
    public static readonly ShipmentCarrier Freterapido = new("FRETERAPIDO");

    /// <summary>
    /// Pitney Bowes.
    /// </summary>
    public static readonly ShipmentCarrier PitneyBowes = new("PITNEY_BOWES");

    /// <summary>
    /// Xpressen courier.
    /// </summary>
    public static readonly ShipmentCarrier XpressenDk = new("XPRESSEN_DK");

    /// <summary>
    /// Spanish Seur API.
    /// </summary>
    public static readonly ShipmentCarrier SeurSpApi = new("SEUR_SP_API");

    /// <summary>
    /// DELIVERYONTIME LOGISTICS PVT LTD.
    /// </summary>
    public static readonly ShipmentCarrier Deliveryontime = new("DELIVERYONTIME");

    /// <summary>
    /// JINSUNG TRADING.
    /// </summary>
    public static readonly ShipmentCarrier Jinsung = new("JINSUNG");

    /// <summary>
    /// Trans Kargo Internasional.
    /// </summary>
    public static readonly ShipmentCarrier TransKargo = new("TRANS_KARGO");

    /// <summary>
    /// Swiship DE.
    /// </summary>
    public static readonly ShipmentCarrier SwishipDe = new("SWISHIP_DE");

    /// <summary>
    /// Ivoy courier.
    /// </summary>
    public static readonly ShipmentCarrier IvoyWebhook = new("IVOY_WEBHOOK");

    /// <summary>
    /// Airmee couriers.
    /// </summary>
    public static readonly ShipmentCarrier AirmeeWebhook = new("AIRMEE_WEBHOOK");

    /// <summary>
    /// dhl benelux.
    /// </summary>
    public static readonly ShipmentCarrier DhlBenelux = new("DHL_BENELUX");

    /// <summary>
    /// FirstMile.
    /// </summary>
    public static readonly ShipmentCarrier Firstmile = new("FIRSTMILE");

    /// <summary>
    /// Fastway Ireland.
    /// </summary>
    public static readonly ShipmentCarrier FastwayIr = new("FASTWAY_IR");

    /// <summary>
    /// Hua Han Logistics.
    /// </summary>
    public static readonly ShipmentCarrier HhExp = new("HH_EXP");

    /// <summary>
    /// Mypostonline.
    /// </summary>
    public static readonly ShipmentCarrier MysMypostOnline = new("MYS_MYPOST_ONLINE");

    /// <summary>
    /// THT Netherland.
    /// </summary>
    public static readonly ShipmentCarrier TntNl = new("TNT_NL");

    /// <summary>
    /// TIPSA courier.
    /// </summary>
    public static readonly ShipmentCarrier Tipsa = new("TIPSA");

    /// <summary>
    /// TAQBIN Malaysia.
    /// </summary>
    public static readonly ShipmentCarrier TaqbinMy = new("TAQBIN_MY");

    /// <summary>
    /// KGM Hub.
    /// </summary>
    public static readonly ShipmentCarrier Kgmhub = new("KGMHUB");

    /// <summary>
    /// Internet Express.
    /// </summary>
    public static readonly ShipmentCarrier Intexpress = new("INTEXPRESS");

    /// <summary>
    /// Overseas Express.
    /// </summary>
    public static readonly ShipmentCarrier OverseExp = new("OVERSE_EXP");

    /// <summary>
    /// One click delivery services.
    /// </summary>
    public static readonly ShipmentCarrier Oneclick = new("ONECLICK");

    /// <summary>
    /// Roadbull Logistics.
    /// </summary>
    public static readonly ShipmentCarrier RoadrunnerFreight = new("ROADRUNNER_FREIGHT");

    /// <summary>
    /// GLS Croatia.
    /// </summary>
    public static readonly ShipmentCarrier GlsCrotia = new("GLS_CROTIA");

    /// <summary>
    /// MRW courier.
    /// </summary>
    public static readonly ShipmentCarrier MrwFtp = new("MRW_FTP");

    /// <summary>
    /// Blue Express.
    /// </summary>
    public static readonly ShipmentCarrier Bluex = new("BLUEX");

    /// <summary>
    /// Daylight Transport.
    /// </summary>
    public static readonly ShipmentCarrier Dylt = new("DYLT");

    /// <summary>
    /// DPD Ireland.
    /// </summary>
    public static readonly ShipmentCarrier DpdIr = new("DPD_IR");

    /// <summary>
    /// Sin Global Express.
    /// </summary>
    public static readonly ShipmentCarrier SinGlbl = new("SIN_GLBL");

    /// <summary>
    /// Tuffnells Parcels Express- Reference.
    /// </summary>
    public static readonly ShipmentCarrier TuffnellsReference = new("TUFFNELLS_REFERENCE");

    /// <summary>
    /// CJ Packet.
    /// </summary>
    public static readonly ShipmentCarrier Cjpacket = new("CJPACKET");

    /// <summary>
    /// Milkman courier.
    /// </summary>
    public static readonly ShipmentCarrier Milkman = new("MILKMAN");

    /// <summary>
    /// ASIGNA courier.
    /// </summary>
    public static readonly ShipmentCarrier Asigna = new("ASIGNA");

    /// <summary>
    /// One World Express.
    /// </summary>
    public static readonly ShipmentCarrier Oneworldexpress = new("ONEWORLDEXPRESS");

    /// <summary>
    /// RoyalShipments.
    /// </summary>
    public static readonly ShipmentCarrier RoyalMail = new("ROYAL_MAIL");

    /// <summary>
    /// Viaxpress.
    /// </summary>
    public static readonly ShipmentCarrier ViaExpress = new("VIA_EXPRESS");

    /// <summary>
    /// TIG Freight.
    /// </summary>
    public static readonly ShipmentCarrier Tigfreight = new("TIGFREIGHT");

    /// <summary>
    /// ZTO Express.
    /// </summary>
    public static readonly ShipmentCarrier ZtoExpress = new("ZTO_EXPRESS");

    /// <summary>
    /// 2GO Courier.
    /// </summary>
    public static readonly ShipmentCarrier TwoGo = new("TWO_GO");

    /// <summary>
    /// IML courier.
    /// </summary>
    public static readonly ShipmentCarrier Iml = new("IML");

    /// <summary>
    /// Intel-Valley Supply chain (ShenZhen) Co. Ltd.
    /// </summary>
    public static readonly ShipmentCarrier IntelValley = new("INTEL_VALLEY");

    /// <summary>
    /// EFS (E-commerce Fulfillment Service).
    /// </summary>
    public static readonly ShipmentCarrier Efs = new("EFS");

    /// <summary>
    /// UK mail (ukmail.com).
    /// </summary>
    public static readonly ShipmentCarrier UkUkMail = new("UK_UK_MAIL");

    /// <summary>
    /// RAM courier.
    /// </summary>
    public static readonly ShipmentCarrier Ram = new("RAM");

    /// <summary>
    /// Allied Express.
    /// </summary>
    public static readonly ShipmentCarrier Alliedexpress = new("ALLIEDEXPRESS");

    /// <summary>
    /// APC overnight (apc-overnight.com).
    /// </summary>
    public static readonly ShipmentCarrier ApcOvernight = new("APC_OVERNIGHT");

    /// <summary>
    /// Shippit.
    /// </summary>
    public static readonly ShipmentCarrier Shippit = new("SHIPPIT");

    /// <summary>
    /// TFM Xpress.
    /// </summary>
    public static readonly ShipmentCarrier Tfm = new("TFM");

    /// <summary>
    /// M Xpress Sdn Bhd.
    /// </summary>
    public static readonly ShipmentCarrier MXpress = new("M_XPRESS");

    /// <summary>
    /// Haidaibao (BOX).
    /// </summary>
    public static readonly ShipmentCarrier HdbBox = new("HDB_BOX");

    /// <summary>
    /// Clevy Links.
    /// </summary>
    public static readonly ShipmentCarrier ClevyLinks = new("CLEVY_LINKS");

    /// <summary>
    /// Beone Logistics.
    /// </summary>
    public static readonly ShipmentCarrier Ibeone = new("IBEONE");

    /// <summary>
    /// Fiege Netherlands.
    /// </summary>
    public static readonly ShipmentCarrier FiegeNl = new("FIEGE_NL");

    /// <summary>
    /// KWE Global.
    /// </summary>
    public static readonly ShipmentCarrier KweGlobal = new("KWE_GLOBAL");

    /// <summary>
    /// CTC Express.
    /// </summary>
    public static readonly ShipmentCarrier CtcExpress = new("CTC_EXPRESS");

    /// <summary>
    /// Amazon Shipping.
    /// </summary>
    public static readonly ShipmentCarrier Amazon = new("AMAZON");

    /// <summary>
    /// Morelink.
    /// </summary>
    public static readonly ShipmentCarrier MoreLink = new("MORE_LINK");

    /// <summary>
    /// JX courier.
    /// </summary>
    public static readonly ShipmentCarrier Jx = new("JX");

    /// <summary>
    /// Easy Mail.
    /// </summary>
    public static readonly ShipmentCarrier EasyMail = new("EASY_MAIL");

    /// <summary>
    /// A Duie Pyle.
    /// </summary>
    public static readonly ShipmentCarrier Aduiepyle = new("ADUIEPYLE");

    /// <summary>
    /// Panther.
    /// </summary>
    public static readonly ShipmentCarrier GbPanther = new("GB_PANTHER");

    /// <summary>
    /// Expresssale.
    /// </summary>
    public static readonly ShipmentCarrier Expresssale = new("EXPRESSSALE");

    /// <summary>
    /// Detrack.
    /// </summary>
    public static readonly ShipmentCarrier SgDetrack = new("SG_DETRACK");

    /// <summary>
    /// Trunkrs courier.
    /// </summary>
    public static readonly ShipmentCarrier TrunkrsWebhook = new("TRUNKRS_WEBHOOK");

    /// <summary>
    /// Matdespatch.
    /// </summary>
    public static readonly ShipmentCarrier Matdespatch = new("MATDESPATCH");

    /// <summary>
    /// GLS Logistic Systems Canada Ltd./Dicom.
    /// </summary>
    public static readonly ShipmentCarrier Dicom = new("DICOM");

    /// <summary>
    /// MBW Courier Inc..
    /// </summary>
    public static readonly ShipmentCarrier Mbw = new("MBW");

    /// <summary>
    /// Cambodia Post.
    /// </summary>
    public static readonly ShipmentCarrier KhmCambodiaPost = new("KHM_CAMBODIA_POST");

    /// <summary>
    /// Sinotrans.
    /// </summary>
    public static readonly ShipmentCarrier Sinotrans = new("SINOTRANS");

    /// <summary>
    /// BRT Bartolini(Parcel ID).
    /// </summary>
    public static readonly ShipmentCarrier BrtItParcelid = new("BRT_IT_PARCELID");

    /// <summary>
    /// DHL Supply Chain APAC.
    /// </summary>
    public static readonly ShipmentCarrier DhlSupplyChain = new("DHL_SUPPLY_CHAIN");

    /// <summary>
    /// DHL Poland.
    /// </summary>
    public static readonly ShipmentCarrier DhlPl = new("DHL_PL");

    /// <summary>
    /// TopYou.
    /// </summary>
    public static readonly ShipmentCarrier Topyou = new("TOPYOU");

    /// <summary>
    /// PAL Express Limited.
    /// </summary>
    public static readonly ShipmentCarrier Palexpress = new("PALEXPRESS");

    /// <summary>
    /// dhl Singapore.
    /// </summary>
    public static readonly ShipmentCarrier DhlSg = new("DHL_SG");

    /// <summary>
    /// WeDo Logistics.
    /// </summary>
    public static readonly ShipmentCarrier CnWedo = new("CN_WEDO");

    /// <summary>
    /// Fulfillme.
    /// </summary>
    public static readonly ShipmentCarrier Fulfillme = new("FULFILLME");

    /// <summary>
    /// DPD delistrack.
    /// </summary>
    public static readonly ShipmentCarrier DpdDelistrack = new("DPD_DELISTRACK");

    /// <summary>
    /// UPS Reference.
    /// </summary>
    public static readonly ShipmentCarrier UpsReference = new("UPS_REFERENCE");

    /// <summary>
    /// Caribou.
    /// </summary>
    public static readonly ShipmentCarrier Caribou = new("CARIBOU");

    /// <summary>
    /// Locus courier.
    /// </summary>
    public static readonly ShipmentCarrier LocusWebhook = new("LOCUS_WEBHOOK");

    /// <summary>
    /// DSV courier.
    /// </summary>
    public static readonly ShipmentCarrier Dsv = new("DSV");

    /// <summary>
    /// P2P TrakPak.
    /// </summary>
    public static readonly ShipmentCarrier P2PTrc = new("P2P_TRC");

    /// <summary>
    /// Direct Parcels.
    /// </summary>
    public static readonly ShipmentCarrier Directparcels = new("DIRECTPARCELS");

    /// <summary>
    /// Nova Poshta (International).
    /// </summary>
    public static readonly ShipmentCarrier NovaPoshtaInt = new("NOVA_POSHTA_INT");

    /// <summary>
    /// FedEx® Poland Domestic.
    /// </summary>
    public static readonly ShipmentCarrier FedexPoland = new("FEDEX_POLAND");

    /// <summary>
    /// JCEX courier.
    /// </summary>
    public static readonly ShipmentCarrier CnJcex = new("CN_JCEX");

    /// <summary>
    /// FAR international.
    /// </summary>
    public static readonly ShipmentCarrier FarInternational = new("FAR_INTERNATIONAL");

    /// <summary>
    /// IDEX courier.
    /// </summary>
    public static readonly ShipmentCarrier Idexpress = new("IDEXPRESS");

    /// <summary>
    /// GANGBAO Supplychain.
    /// </summary>
    public static readonly ShipmentCarrier Gangbao = new("GANGBAO");

    /// <summary>
    /// Neway Transport.
    /// </summary>
    public static readonly ShipmentCarrier Neway = new("NEWAY");

    /// <summary>
    /// PostNL International.
    /// </summary>
    public static readonly ShipmentCarrier PostnlInt3S = new("POSTNL_INT_3_S");

    /// <summary>
    /// RPX Indonesia.
    /// </summary>
    public static readonly ShipmentCarrier RpxId = new("RPX_ID");

    /// <summary>
    /// Designer Transport.
    /// </summary>
    public static readonly ShipmentCarrier DesignertransportWebhook = new("DESIGNERTRANSPORT_WEBHOOK");

    /// <summary>
    /// GLS Slovenia.
    /// </summary>
    public static readonly ShipmentCarrier GlsSloven = new("GLS_SLOVEN");

    /// <summary>
    /// Parcelled.in.
    /// </summary>
    public static readonly ShipmentCarrier ParcelledIn = new("PARCELLED_IN");

    /// <summary>
    /// GSI EXPRESS.
    /// </summary>
    public static readonly ShipmentCarrier GsiExpress = new("GSI_EXPRESS");

    /// <summary>
    /// Con-way Freight.
    /// </summary>
    public static readonly ShipmentCarrier ConWay = new("CON_WAY");

    /// <summary>
    /// Brouwer Transport en Logistiek.
    /// </summary>
    public static readonly ShipmentCarrier BrouwerTransport = new("BROUWER_TRANSPORT");

    /// <summary>
    /// Captain Express International.
    /// </summary>
    public static readonly ShipmentCarrier Cpex = new("CPEX");

    /// <summary>
    /// Israel Post.
    /// </summary>
    public static readonly ShipmentCarrier IsraelPost = new("ISRAEL_POST");

    /// <summary>
    /// DTDC India.
    /// </summary>
    public static readonly ShipmentCarrier DtdcIn = new("DTDC_IN");

    /// <summary>
    /// PTT Post.
    /// </summary>
    public static readonly ShipmentCarrier PttPost = new("PTT_POST");

    /// <summary>
    /// Ximex Delivery Express.
    /// </summary>
    public static readonly ShipmentCarrier XdeWebhook = new("XDE_WEBHOOK");

    /// <summary>
    /// Tolos courier.
    /// </summary>
    public static readonly ShipmentCarrier Tolos = new("TOLOS");

    /// <summary>
    /// Giao hàng nhanh.
    /// </summary>
    public static readonly ShipmentCarrier GiaoHang = new("GIAO_HANG");

    /// <summary>
    /// Geodis E-space.
    /// </summary>
    public static readonly ShipmentCarrier GeodisEspace = new("GEODIS_ESPACE");

    /// <summary>
    /// Magyar Post.
    /// </summary>
    public static readonly ShipmentCarrier MagyarHu = new("MAGYAR_HU");

    /// <summary>
    /// DoorDash.
    /// </summary>
    public static readonly ShipmentCarrier DoordashWebhook = new("DOORDASH_WEBHOOK");

    /// <summary>
    /// Tiki shipment.
    /// </summary>
    public static readonly ShipmentCarrier TikiId = new("TIKI_ID");

    /// <summary>
    /// CJ Logistics International(Hong Kong).
    /// </summary>
    public static readonly ShipmentCarrier CjHkInternational = new("CJ_HK_INTERNATIONAL");

    /// <summary>
    /// Star Track Express.
    /// </summary>
    public static readonly ShipmentCarrier StarTrackExpress = new("STAR_TRACK_EXPRESS");

    /// <summary>
    /// Helthjem.
    /// </summary>
    public static readonly ShipmentCarrier Helthjem = new("HELTHJEM");

    /// <summary>
    /// SF International.
    /// </summary>
    public static readonly ShipmentCarrier Sfb2C = new("SFB2C");

    /// <summary>
    /// Freightquote by C.H. Robinson.
    /// </summary>
    public static readonly ShipmentCarrier Freightquote = new("FREIGHTQUOTE");

    /// <summary>
    /// Landmark Global Reference.
    /// </summary>
    public static readonly ShipmentCarrier LandmarkGlobalReference = new("LANDMARK_GLOBAL_REFERENCE");

    /// <summary>
    /// Parcel2Go.
    /// </summary>
    public static readonly ShipmentCarrier Parcel2Go = new("PARCEL2GO");

    /// <summary>
    /// Delnext.
    /// </summary>
    public static readonly ShipmentCarrier Delnext = new("DELNEXT");

    /// <summary>
    /// Red Carpet Logistics.
    /// </summary>
    public static readonly ShipmentCarrier Rcl = new("RCL");

    /// <summary>
    /// CGS Express.
    /// </summary>
    public static readonly ShipmentCarrier CgsExpress = new("CGS_EXPRESS");

    /// <summary>
    /// Hongkong Post (www.hongkongpost.hk).
    /// </summary>
    public static readonly ShipmentCarrier HkPost = new("HK_POST");

    /// <summary>
    /// SAP EXPRESS.
    /// </summary>
    public static readonly ShipmentCarrier SapExpress = new("SAP_EXPRESS");

    /// <summary>
    /// Parcel Post Singapore.
    /// </summary>
    public static readonly ShipmentCarrier ParcelpostSg = new("PARCELPOST_SG");

    /// <summary>
    /// HermesWorld UK.
    /// </summary>
    public static readonly ShipmentCarrier Hermes = new("HERMES");

    /// <summary>
    /// Safexpress.
    /// </summary>
    public static readonly ShipmentCarrier IndSafeexpress = new("IND_SAFEEXPRESS");

    /// <summary>
    /// Tophatter Express.
    /// </summary>
    public static readonly ShipmentCarrier Tophatterexpress = new("TOPHATTEREXPRESS");

    /// <summary>
    /// PT MGLOBAL LOGISTICS INDONESIA.
    /// </summary>
    public static readonly ShipmentCarrier Mglobal = new("MGLOBAL");

    /// <summary>
    /// Averitt Express.
    /// </summary>
    public static readonly ShipmentCarrier Averitt = new("AVERITT");

    /// <summary>
    /// leader.
    /// </summary>
    public static readonly ShipmentCarrier Leader = new("LEADER");

    /// <summary>
    /// 2ebox courier.
    /// </summary>
    public static readonly ShipmentCarrier _2Ebox = new("_2EBOX");

    /// <summary>
    /// Singapore Speedpost.
    /// </summary>
    public static readonly ShipmentCarrier SgSpeedpost = new("SG_SPEEDPOST");

    /// <summary>
    /// DB Schenker (www.dbschenker.com).
    /// </summary>
    public static readonly ShipmentCarrier DbschenkerSe = new("DBSCHENKER_SE");

    /// <summary>
    /// Israel Post Domestic.
    /// </summary>
    public static readonly ShipmentCarrier IsrPostDomestic = new("ISR_POST_DOMESTIC");

    /// <summary>
    /// Best Way Parcel.
    /// </summary>
    public static readonly ShipmentCarrier Bestwayparcel = new("BESTWAYPARCEL");

    /// <summary>
    /// asendia_de.
    /// </summary>
    public static readonly ShipmentCarrier AsendiaDe = new("ASENDIA_DE");

    /// <summary>
    /// nightline_uk.
    /// </summary>
    public static readonly ShipmentCarrier NightlineUk = new("NIGHTLINE_UK");

    /// <summary>
    /// taqbin_sg.
    /// </summary>
    public static readonly ShipmentCarrier TaqbinSg = new("TAQBIN_SG");

    /// <summary>
    /// TCK Express.
    /// </summary>
    public static readonly ShipmentCarrier TckExpress = new("TCK_EXPRESS");

    /// <summary>
    /// Endeavour Delivery.
    /// </summary>
    public static readonly ShipmentCarrier EndeavourDelivery = new("ENDEAVOUR_DELIVERY");

    /// <summary>
    /// Nanjing Woyuan.
    /// </summary>
    public static readonly ShipmentCarrier Nanjingwoyuan = new("NANJINGWOYUAN");

    /// <summary>
    /// Heppner France.
    /// </summary>
    public static readonly ShipmentCarrier HeppnerFr = new("HEPPNER_FR");

    /// <summary>
    /// EMPS Express.
    /// </summary>
    public static readonly ShipmentCarrier EmpsCn = new("EMPS_CN");

    /// <summary>
    /// Fonsen Logistics.
    /// </summary>
    public static readonly ShipmentCarrier Fonsen = new("FONSEN");

    /// <summary>
    /// Pickrr.
    /// </summary>
    public static readonly ShipmentCarrier Pickrr = new("PICKRR");

    /// <summary>
    /// APC Overnight Consignment.
    /// </summary>
    public static readonly ShipmentCarrier ApcOvernightConnum = new("APC_OVERNIGHT_CONNUM");

    /// <summary>
    /// Star Track Next Flight.
    /// </summary>
    public static readonly ShipmentCarrier StarTrackNextFlight = new("STAR_TRACK_NEXT_FLIGHT");

    /// <summary>
    /// Shanghai Aqrum Chemical Logistics Co.Ltd.
    /// </summary>
    public static readonly ShipmentCarrier Dajin = new("DAJIN");

    /// <summary>
    /// UPS Freight.
    /// </summary>
    public static readonly ShipmentCarrier UpsFreight = new("UPS_FREIGHT");

    /// <summary>
    /// Posta Plus.
    /// </summary>
    public static readonly ShipmentCarrier PostaPlus = new("POSTA_PLUS");

    /// <summary>
    /// CEVA LOGISTICS.
    /// </summary>
    public static readonly ShipmentCarrier Ceva = new("CEVA");

    /// <summary>
    /// ANSERX courier.
    /// </summary>
    public static readonly ShipmentCarrier Anserx = new("ANSERX");

    /// <summary>
    /// JS EXPRESS.
    /// </summary>
    public static readonly ShipmentCarrier JsExpress = new("JS_EXPRESS");

    /// <summary>
    /// padtf.com.
    /// </summary>
    public static readonly ShipmentCarrier Padtf = new("PADTF");

    /// <summary>
    /// UPS Mail Innovations.
    /// </summary>
    public static readonly ShipmentCarrier UpsMailInnovations = new("UPS_MAIL_INNOVATIONS");

    /// <summary>
    /// Sunyou Post.
    /// </summary>
    public static readonly ShipmentCarrier Sypost = new("SYPOST");

    /// <summary>
    /// Amazon Shipping + Amazon MCF.
    /// </summary>
    public static readonly ShipmentCarrier AmazonShipMcf = new("AMAZON_SHIP_MCF");

    /// <summary>
    /// Yusen Logistics.
    /// </summary>
    public static readonly ShipmentCarrier Yusen = new("YUSEN");

    /// <summary>
    /// Bring.
    /// </summary>
    public static readonly ShipmentCarrier Bring = new("BRING");

    /// <summary>
    /// SDA Italy.
    /// </summary>
    public static readonly ShipmentCarrier SdaIt = new("SDA_IT");

    /// <summary>
    /// GBA Services Ltd.
    /// </summary>
    public static readonly ShipmentCarrier Gba = new("GBA");

    /// <summary>
    /// Newegg Express.
    /// </summary>
    public static readonly ShipmentCarrier Neweggexpress = new("NEWEGGEXPRESS");

    /// <summary>
    /// Speed Couriers.
    /// </summary>
    public static readonly ShipmentCarrier SpeedcouriersGr = new("SPEEDCOURIERS_GR");

    /// <summary>
    /// forrun Pvt Ltd (Arpatech Venture).
    /// </summary>
    public static readonly ShipmentCarrier Forrun = new("FORRUN");

    /// <summary>
    /// Pickupp.
    /// </summary>
    public static readonly ShipmentCarrier Pickup = new("PICKUP");

    /// <summary>
    /// ECMS International Logistics Co..
    /// </summary>
    public static readonly ShipmentCarrier Ecms = new("ECMS");

    /// <summary>
    /// Intelipost (TMS for LATAM).
    /// </summary>
    public static readonly ShipmentCarrier Intelipost = new("INTELIPOST");

    /// <summary>
    /// Flash Express.
    /// </summary>
    public static readonly ShipmentCarrier Flashexpress = new("FLASHEXPRESS");

    /// <summary>
    /// STO Express.
    /// </summary>
    public static readonly ShipmentCarrier CnSto = new("CN_STO");

    /// <summary>
    /// SEKO Worldwide.
    /// </summary>
    public static readonly ShipmentCarrier SekoSftp = new("SEKO_SFTP");

    /// <summary>
    /// Home Delivery Solutions Ltd.
    /// </summary>
    public static readonly ShipmentCarrier HomeDeliverySolutions = new("HOME_DELIVERY_SOLUTIONS");

    /// <summary>
    /// DPD Hungary.
    /// </summary>
    public static readonly ShipmentCarrier DpdHgry = new("DPD_HGRY");

    /// <summary>
    /// Kerry Express (Vietnam) Co Ltd.
    /// </summary>
    public static readonly ShipmentCarrier KerryttcVn = new("KERRYTTC_VN");

    /// <summary>
    /// Joying Box.
    /// </summary>
    public static readonly ShipmentCarrier JoyingBox = new("JOYING_BOX");

    /// <summary>
    /// Total Express.
    /// </summary>
    public static readonly ShipmentCarrier TotalExpress = new("TOTAL_EXPRESS");

    /// <summary>
    /// ZJS International.
    /// </summary>
    public static readonly ShipmentCarrier ZjsExpress = new("ZJS_EXPRESS");

    /// <summary>
    /// STARKEN couriers.
    /// </summary>
    public static readonly ShipmentCarrier Starken = new("STARKEN");

    /// <summary>
    /// DemandShip.
    /// </summary>
    public static readonly ShipmentCarrier Demandship = new("DEMANDSHIP");

    /// <summary>
    /// DPEX.
    /// </summary>
    public static readonly ShipmentCarrier CnDpex = new("CN_DPEX");

    /// <summary>
    /// AuPost China.
    /// </summary>
    public static readonly ShipmentCarrier AupostCn = new("AUPOST_CN");

    /// <summary>
    /// Logisters.
    /// </summary>
    public static readonly ShipmentCarrier Logisters = new("LOGISTERS");

    /// <summary>
    /// Global Post.
    /// </summary>
    public static readonly ShipmentCarrier Goglobalpost = new("GOGLOBALPOST");

    /// <summary>
    /// GLS Czech Republic.
    /// </summary>
    public static readonly ShipmentCarrier GlsCz = new("GLS_CZ");

    /// <summary>
    /// Paack courier.
    /// </summary>
    public static readonly ShipmentCarrier PaackWebhook = new("PAACK_WEBHOOK");

    /// <summary>
    /// Grab courier.
    /// </summary>
    public static readonly ShipmentCarrier GrabWebhook = new("GRAB_WEBHOOK");

    /// <summary>
    /// Parcelpoint.
    /// </summary>
    public static readonly ShipmentCarrier Parcelpoint = new("PARCELPOINT");

    /// <summary>
    /// iCumulus.
    /// </summary>
    public static readonly ShipmentCarrier Icumulus = new("ICUMULUS");

    /// <summary>
    /// DAI Post.
    /// </summary>
    public static readonly ShipmentCarrier Daiglobaltrack = new("DAIGLOBALTRACK");

    /// <summary>
    /// i-parcel.
    /// </summary>
    public static readonly ShipmentCarrier GlobalIparcel = new("GLOBAL_IPARCEL");

    /// <summary>
    /// Yurtici Kargo.
    /// </summary>
    public static readonly ShipmentCarrier YurticiKargo = new("YURTICI_KARGO");

    /// <summary>
    /// PayPal Package.
    /// </summary>
    public static readonly ShipmentCarrier CnPaypalPackage = new("CN_PAYPAL_PACKAGE");

    /// <summary>
    /// Parcel To Post.
    /// </summary>
    public static readonly ShipmentCarrier Parcel2Post = new("PARCEL_2_POST");

    /// <summary>
    /// GLS Italy.
    /// </summary>
    public static readonly ShipmentCarrier GlsIt = new("GLS_IT");

    /// <summary>
    /// PIL Logistics (China) Co..
    /// </summary>
    public static readonly ShipmentCarrier PilLogistics = new("PIL_LOGISTICS");

    /// <summary>
    /// Heppner Internationale Spedition GmbH &amp; Co..
    /// </summary>
    public static readonly ShipmentCarrier Heppner = new("HEPPNER");

    /// <summary>
    /// Go!Express and logistics.
    /// </summary>
    public static readonly ShipmentCarrier GeneralOvernight = new("GENERAL_OVERNIGHT");

    /// <summary>
    /// Happy 2ThePoint.
    /// </summary>
    public static readonly ShipmentCarrier Happy2Point = new("HAPPY2POINT");

    /// <summary>
    /// Chit Chats.
    /// </summary>
    public static readonly ShipmentCarrier Chitchats = new("CHITCHATS");

    /// <summary>
    /// Smooth Couriers.
    /// </summary>
    public static readonly ShipmentCarrier Smooth = new("SMOOTH");

    /// <summary>
    /// CL E-Logistics Solutions Limited.
    /// </summary>
    public static readonly ShipmentCarrier CleLogistics = new("CLE_LOGISTICS");

    /// <summary>
    /// Fiege Logistics.
    /// </summary>
    public static readonly ShipmentCarrier Fiege = new("FIEGE");

    /// <summary>
    /// M&amp;X cargo.
    /// </summary>
    public static readonly ShipmentCarrier MxCargo = new("MX_CARGO");

    /// <summary>
    /// Ziing Final Mile Inc.
    /// </summary>
    public static readonly ShipmentCarrier Ziingfinalmile = new("ZIINGFINALMILE");

    /// <summary>
    /// Dayton Freight.
    /// </summary>
    public static readonly ShipmentCarrier DaytonFreight = new("DAYTON_FREIGHT");

    /// <summary>
    /// TCS courier.
    /// </summary>
    public static readonly ShipmentCarrier Tcs = new("TCS");

    /// <summary>
    /// AEX Group.
    /// </summary>
    public static readonly ShipmentCarrier Aex = new("AEX");

    /// <summary>
    /// Hermes Germany.
    /// </summary>
    public static readonly ShipmentCarrier HermesDe = new("HERMES_DE");

    /// <summary>
    /// Routific.
    /// </summary>
    public static readonly ShipmentCarrier RoutificWebhook = new("ROUTIFIC_WEBHOOK");

    /// <summary>
    /// Globavend.
    /// </summary>
    public static readonly ShipmentCarrier Globavend = new("GLOBAVEND");

    /// <summary>
    /// CJ Logistics International.
    /// </summary>
    public static readonly ShipmentCarrier CjLogistics = new("CJ_LOGISTICS");

    /// <summary>
    /// The Pallet Network.
    /// </summary>
    public static readonly ShipmentCarrier PalletNetwork = new("PALLET_NETWORK");

    /// <summary>
    /// RAF Philippines.
    /// </summary>
    public static readonly ShipmentCarrier RafPh = new("RAF_PH");

    /// <summary>
    /// XDP Express.
    /// </summary>
    public static readonly ShipmentCarrier UkXdp = new("UK_XDP");

    /// <summary>
    /// Paper Express.
    /// </summary>
    public static readonly ShipmentCarrier PaperExpress = new("PAPER_EXPRESS");

    /// <summary>
    /// La Poste.
    /// </summary>
    public static readonly ShipmentCarrier LaPosteSuivi = new("LA_POSTE_SUIVI");

    /// <summary>
    /// Paquetexpress.
    /// </summary>
    public static readonly ShipmentCarrier Paquetexpress = new("PAQUETEXPRESS");

    /// <summary>
    /// liefery.
    /// </summary>
    public static readonly ShipmentCarrier Liefery = new("LIEFERY");

    /// <summary>
    /// Streck Transport.
    /// </summary>
    public static readonly ShipmentCarrier StreckTransport = new("STRECK_TRANSPORT");

    /// <summary>
    /// Pony express.
    /// </summary>
    public static readonly ShipmentCarrier PonyExpress = new("PONY_EXPRESS");

    /// <summary>
    /// Always Express.
    /// </summary>
    public static readonly ShipmentCarrier AlwaysExpress = new("ALWAYS_EXPRESS");

    /// <summary>
    /// GBS-Broker.
    /// </summary>
    public static readonly ShipmentCarrier GbsBroker = new("GBS_BROKER");

    /// <summary>
    /// City-Link Express.
    /// </summary>
    public static readonly ShipmentCarrier CitylinkMy = new("CITYLINK_MY");

    /// <summary>
    /// ALLJOY SUPPLY CHAIN.
    /// </summary>
    public static readonly ShipmentCarrier Alljoy = new("ALLJOY");

    /// <summary>
    /// yodel.
    /// </summary>
    public static readonly ShipmentCarrier Yodel = new("YODEL");

    /// <summary>
    /// Yodel Direct.
    /// </summary>
    public static readonly ShipmentCarrier YodelDir = new("YODEL_DIR");

    /// <summary>
    /// STONE3PL.
    /// </summary>
    public static readonly ShipmentCarrier Stone3Pl = new("STONE3PL");

    /// <summary>
    /// ParcelPal.
    /// </summary>
    public static readonly ShipmentCarrier ParcelpalWebhook = new("PARCELPAL_WEBHOOK");

    /// <summary>
    /// DHL eCommerce Asia (API).
    /// </summary>
    public static readonly ShipmentCarrier DhlEcomerceAsa = new("DHL_ECOMERCE_ASA");

    /// <summary>
    /// J&amp;T Express Singapore.
    /// </summary>
    public static readonly ShipmentCarrier Simplypost = new("SIMPLYPOST");

    /// <summary>
    /// Kua Yue Express.
    /// </summary>
    public static readonly ShipmentCarrier KyExpress = new("KY_EXPRESS");

    /// <summary>
    /// shenzhen 1st International Logistics(Group)Co.
    /// </summary>
    public static readonly ShipmentCarrier Shenzhen = new("SHENZHEN");

    /// <summary>
    /// LaserShip.
    /// </summary>
    public static readonly ShipmentCarrier UsLasership = new("US_LASERSHIP");

    /// <summary>
    /// ucexpress.
    /// </summary>
    public static readonly ShipmentCarrier UcExpre = new("UC_EXPRE");

    /// <summary>
    /// DIDADI Logistics tech.
    /// </summary>
    public static readonly ShipmentCarrier Didadi = new("DIDADI");

    /// <summary>
    /// CJ Korea Express.
    /// </summary>
    public static readonly ShipmentCarrier CjKr = new("CJ_KR");

    /// <summary>
    /// DB Schenker B2B.
    /// </summary>
    public static readonly ShipmentCarrier DbschenkerB2B = new("DBSCHENKER_B2B");

    /// <summary>
    /// MXE Express.
    /// </summary>
    public static readonly ShipmentCarrier Mxe = new("MXE");

    /// <summary>
    /// CAE Delivers.
    /// </summary>
    public static readonly ShipmentCarrier CaeDelivers = new("CAE_DELIVERS");

    /// <summary>
    /// PFC Express.
    /// </summary>
    public static readonly ShipmentCarrier Pfcexpress = new("PFCEXPRESS");

    /// <summary>
    /// Whistl.
    /// </summary>
    public static readonly ShipmentCarrier Whistl = new("WHISTL");

    /// <summary>
    /// WePost Sdn Bhd.
    /// </summary>
    public static readonly ShipmentCarrier Wepost = new("WEPOST");

    /// <summary>
    /// DHL parcel Spain(www.dhl.com).
    /// </summary>
    public static readonly ShipmentCarrier DhlParcelEs = new("DHL_PARCEL_ES");

    /// <summary>
    /// DD Express Courier.
    /// </summary>
    public static readonly ShipmentCarrier Ddexpress = new("DDEXPRESS");

    /// <summary>
    /// Aramex Australia (formerly Fastway AU).
    /// </summary>
    public static readonly ShipmentCarrier AramexAu = new("ARAMEX_AU");

    /// <summary>
    /// Bneed courier.
    /// </summary>
    public static readonly ShipmentCarrier Bneed = new("BNEED");

    /// <summary>
    /// Kerry Express Hong Kong.
    /// </summary>
    public static readonly ShipmentCarrier HkTgx = new("HK_TGX");

    /// <summary>
    /// Latvijas Pasts.
    /// </summary>
    public static readonly ShipmentCarrier LatvijasPasts = new("LATVIJAS_PASTS");

    /// <summary>
    /// ViaEurope.
    /// </summary>
    public static readonly ShipmentCarrier Viaeurope = new("VIAEUROPE");

    /// <summary>
    /// Correo Uruguayo.
    /// </summary>
    public static readonly ShipmentCarrier CorreoUy = new("CORREO_UY");

    /// <summary>
    /// Chronopost france (www.chronopost.fr).
    /// </summary>
    public static readonly ShipmentCarrier ChronopostFr = new("CHRONOPOST_FR");

    /// <summary>
    /// J-Net.
    /// </summary>
    public static readonly ShipmentCarrier JNet = new("J_NET");

    /// <summary>
    /// 6ls.com.
    /// </summary>
    public static readonly ShipmentCarrier _6Ls = new("_6LS");

    /// <summary>
    /// Belpost.
    /// </summary>
    public static readonly ShipmentCarrier BlrBelpost = new("BLR_BELPOST");

    /// <summary>
    /// BirdSystem.
    /// </summary>
    public static readonly ShipmentCarrier Birdsystem = new("BIRDSYSTEM");

    /// <summary>
    /// DobroPost.
    /// </summary>
    public static readonly ShipmentCarrier Dobropost = new("DOBROPOST");

    /// <summary>
    /// Wahana express (www.wahana.com).
    /// </summary>
    public static readonly ShipmentCarrier WahanaId = new("WAHANA_ID");

    /// <summary>
    /// Weaship.
    /// </summary>
    public static readonly ShipmentCarrier Weaship = new("WEASHIP");

    /// <summary>
    /// Sonic Transportation &amp; Logistics.
    /// </summary>
    public static readonly ShipmentCarrier Sonictl = new("SONICTL");

    /// <summary>
    /// Shenzhen Jinghuada Logistics Co..
    /// </summary>
    public static readonly ShipmentCarrier Kwt = new("KWT");

    /// <summary>
    /// AFL LOGISTICS.
    /// </summary>
    public static readonly ShipmentCarrier AfllogFtp = new("AFLLOG_FTP");

    /// <summary>
    /// SkyNet Worldwide Express.
    /// </summary>
    public static readonly ShipmentCarrier SkynetWorldwide = new("SKYNET_WORLDWIDE");

    /// <summary>
    /// Nova Poshta (novaposhta.ua).
    /// </summary>
    public static readonly ShipmentCarrier NovaPoshta = new("NOVA_POSHTA");

    /// <summary>
    /// Seino.
    /// </summary>
    public static readonly ShipmentCarrier Seino = new("SEINO");

    /// <summary>
    /// SZENDEX.
    /// </summary>
    public static readonly ShipmentCarrier Szendex = new("SZENDEX");

    /// <summary>
    /// Bpost international.
    /// </summary>
    public static readonly ShipmentCarrier BpostInt = new("BPOST_INT");

    /// <summary>
    /// DB Schenker Sweden.
    /// </summary>
    public static readonly ShipmentCarrier DbschenkerSv = new("DBSCHENKER_SV");

    /// <summary>
    /// AO Deutschland.
    /// </summary>
    public static readonly ShipmentCarrier AoDeutschland = new("AO_DEUTSCHLAND");

    /// <summary>
    /// EU Fleet Solutions.
    /// </summary>
    public static readonly ShipmentCarrier EuFleetSolutions = new("EU_FLEET_SOLUTIONS");

    /// <summary>
    /// PCF Final Mile.
    /// </summary>
    public static readonly ShipmentCarrier Pcfcorp = new("PCFCORP");

    /// <summary>
    /// Link Bridge(BeiJing)international logistics co..
    /// </summary>
    public static readonly ShipmentCarrier Linkbridge = new("LINKBRIDGE");

    /// <summary>
    /// PT Prima Multi Cipta.
    /// </summary>
    public static readonly ShipmentCarrier Primamulticipta = new("PRIMAMULTICIPTA");

    /// <summary>
    /// Urbanfox.
    /// </summary>
    public static readonly ShipmentCarrier Courex = new("COUREX");

    /// <summary>
    /// Zajil Express Company.
    /// </summary>
    public static readonly ShipmentCarrier ZajilExpress = new("ZAJIL_EXPRESS");

    /// <summary>
    /// CollectCo.
    /// </summary>
    public static readonly ShipmentCarrier Collectco = new("COLLECTCO");

    /// <summary>
    /// J&amp;T EXPRESS MALAYSIA.
    /// </summary>
    public static readonly ShipmentCarrier Jtexpress = new("JTEXPRESS");

    /// <summary>
    /// FedEx® UK.
    /// </summary>
    public static readonly ShipmentCarrier FedexUk = new("FEDEX_UK");

    /// <summary>
    /// uShip courier.
    /// </summary>
    public static readonly ShipmentCarrier Uship = new("USHIP");

    /// <summary>
    /// PIXSELL LOGISTICS.
    /// </summary>
    public static readonly ShipmentCarrier Pixsell = new("PIXSELL");

    /// <summary>
    /// Shiptor.
    /// </summary>
    public static readonly ShipmentCarrier Shiptor = new("SHIPTOR");

    /// <summary>
    /// CDEK courier.
    /// </summary>
    public static readonly ShipmentCarrier Cdek = new("CDEK");

    /// <summary>
    /// ViettelPost.
    /// </summary>
    public static readonly ShipmentCarrier VnmViettelpost = new("VNM_VIETTELPOST");

    /// <summary>
    /// CJ Century.
    /// </summary>
    public static readonly ShipmentCarrier CjCentury = new("CJ_CENTURY");

    /// <summary>
    /// GSO(GLS-USA).
    /// </summary>
    public static readonly ShipmentCarrier Gso = new("GSO");

    /// <summary>
    /// VIWO IoT.
    /// </summary>
    public static readonly ShipmentCarrier Viwo = new("VIWO");

    /// <summary>
    /// SKYBOX.
    /// </summary>
    public static readonly ShipmentCarrier Skybox = new("SKYBOX");

    /// <summary>
    /// Kerry TJ Logistics.
    /// </summary>
    public static readonly ShipmentCarrier Kerrytj = new("KERRYTJ");

    /// <summary>
    /// Nhat Tin Logistics.
    /// </summary>
    public static readonly ShipmentCarrier NtlogisticsVn = new("NTLOGISTICS_VN");

    /// <summary>
    /// lightning monkey.
    /// </summary>
    public static readonly ShipmentCarrier SdhScm = new("SDH_SCM");

    /// <summary>
    /// Zinc courier.
    /// </summary>
    public static readonly ShipmentCarrier Zinc = new("ZINC");

    /// <summary>
    /// DPE South Africa.
    /// </summary>
    public static readonly ShipmentCarrier DpeSouthAfrc = new("DPE_SOUTH_AFRC");

    /// <summary>
    /// Czech Post.
    /// </summary>
    public static readonly ShipmentCarrier CeskaCz = new("CESKA_CZ");

    /// <summary>
    /// ACS Courier.
    /// </summary>
    public static readonly ShipmentCarrier AcsGr = new("ACS_GR");

    /// <summary>
    /// DealerSend.
    /// </summary>
    public static readonly ShipmentCarrier Dealersend = new("DEALERSEND");

    /// <summary>
    /// Jocom.
    /// </summary>
    public static readonly ShipmentCarrier Jocom = new("JOCOM");

    /// <summary>
    /// CSE courier.
    /// </summary>
    public static readonly ShipmentCarrier Cse = new("CSE");

    /// <summary>
    /// TForce Final Mile.
    /// </summary>
    public static readonly ShipmentCarrier TforceFinalmile = new("TFORCE_FINALMILE");

    /// <summary>
    /// ShipGate.
    /// </summary>
    public static readonly ShipmentCarrier ShipGate = new("SHIP_GATE");

    /// <summary>
    /// SHIPTER.
    /// </summary>
    public static readonly ShipmentCarrier Shipter = new("SHIPTER");

    /// <summary>
    /// National Sameday.
    /// </summary>
    public static readonly ShipmentCarrier NationalSameday = new("NATIONAL_SAMEDAY");

    /// <summary>
    /// YunExpress.
    /// </summary>
    public static readonly ShipmentCarrier Yunexpress = new("YUNEXPRESS");

    /// <summary>
    /// AliExpress Standard Shipping.
    /// </summary>
    public static readonly ShipmentCarrier Cainiao = new("CAINIAO");

    /// <summary>
    /// DMSMatrix.
    /// </summary>
    public static readonly ShipmentCarrier DmsMatrix = new("DMS_MATRIX");

    /// <summary>
    /// Directlog (www.directlog.com.br).
    /// </summary>
    public static readonly ShipmentCarrier Directlog = new("DIRECTLOG");

    /// <summary>
    /// Asendia USA.
    /// </summary>
    public static readonly ShipmentCarrier AsendiaUs = new("ASENDIA_US");

    /// <summary>
    /// 3JMS Logistics.
    /// </summary>
    public static readonly ShipmentCarrier _3Jmslogistics = new("_3JMSLOGISTICS");

    /// <summary>
    /// LICCARDI EXPRESS COURIER.
    /// </summary>
    public static readonly ShipmentCarrier LiccardiExpress = new("LICCARDI_EXPRESS");

    /// <summary>
    /// SkyPostal.
    /// </summary>
    public static readonly ShipmentCarrier SkyPostal = new("SKY_POSTAL");

    /// <summary>
    /// cnwangtong.
    /// </summary>
    public static readonly ShipmentCarrier Cnwangtong = new("CNWANGTONG");

    /// <summary>
    /// ostnord denmark.
    /// </summary>
    public static readonly ShipmentCarrier PostnordLogisticsDk = new("POSTNORD_LOGISTICS_DK");

    /// <summary>
    /// Logistika.
    /// </summary>
    public static readonly ShipmentCarrier Logistika = new("LOGISTIKA");

    /// <summary>
    /// Celeritas Transporte.
    /// </summary>
    public static readonly ShipmentCarrier Celeritas = new("CELERITAS");

    /// <summary>
    /// Pressio.
    /// </summary>
    public static readonly ShipmentCarrier Pressiode = new("PRESSIODE");

    /// <summary>
    /// Shree Maruti Courier Services Pvt Ltd.
    /// </summary>
    public static readonly ShipmentCarrier ShreeMaruti = new("SHREE_MARUTI");

    /// <summary>
    /// Logistic Worldwide Express (LWE Honkong).
    /// </summary>
    public static readonly ShipmentCarrier LogisticsworldwideHk = new("LOGISTICSWORLDWIDE_HK");

    /// <summary>
    /// eFEx (E-Commerce Fulfillment &amp; Express).
    /// </summary>
    public static readonly ShipmentCarrier Efex = new("EFEX");

    /// <summary>
    /// Lotte Global Logistics.
    /// </summary>
    public static readonly ShipmentCarrier Lotte = new("LOTTE");

    /// <summary>
    /// Lone Star Overnight.
    /// </summary>
    public static readonly ShipmentCarrier Lonestar = new("LONESTAR");

    /// <summary>
    /// Aprisa Express.
    /// </summary>
    public static readonly ShipmentCarrier Aprisaexpress = new("APRISAEXPRESS");

    /// <summary>
    /// BEL North Russia.
    /// </summary>
    public static readonly ShipmentCarrier BelRs = new("BEL_RS");

    /// <summary>
    /// OSM Worldwide.
    /// </summary>
    public static readonly ShipmentCarrier OsmWorldwide = new("OSM_WORLDWIDE");

    /// <summary>
    /// Westgate Global.
    /// </summary>
    public static readonly ShipmentCarrier WestgateGl = new("WESTGATE_GL");

    /// <summary>
    /// Fasttrack.
    /// </summary>
    public static readonly ShipmentCarrier Fastrack = new("FASTRACK");

    /// <summary>
    /// DTD Express.
    /// </summary>
    public static readonly ShipmentCarrier DtdExpr = new("DTD_EXPR");

    /// <summary>
    /// AlfaTrex.
    /// </summary>
    public static readonly ShipmentCarrier Alfatrex = new("ALFATREX");

    /// <summary>
    /// ProMed Delivery.
    /// </summary>
    public static readonly ShipmentCarrier Promeddelivery = new("PROMEDDELIVERY");

    /// <summary>
    /// Thabit Logistics.
    /// </summary>
    public static readonly ShipmentCarrier ThabitLogistics = new("THABIT_LOGISTICS");

    /// <summary>
    /// HCT LOGISTICS CO.LTD..
    /// </summary>
    public static readonly ShipmentCarrier HctLogistics = new("HCT_LOGISTICS");

    /// <summary>
    /// Carry-Flap Co..
    /// </summary>
    public static readonly ShipmentCarrier CarryFlap = new("CARRY_FLAP");

    /// <summary>
    /// Old Dominion Freight Line.
    /// </summary>
    public static readonly ShipmentCarrier UsOldDominion = new("US_OLD_DOMINION");

    /// <summary>
    /// ANICAM BOX EXPRESS.
    /// </summary>
    public static readonly ShipmentCarrier AnicamBox = new("ANICAM_BOX");

    /// <summary>
    /// WanbExpress.
    /// </summary>
    public static readonly ShipmentCarrier Wanbexpress = new("WANBEXPRESS");

    /// <summary>
    /// An Post.
    /// </summary>
    public static readonly ShipmentCarrier AnPost = new("AN_POST");

    /// <summary>
    /// DPD Local.
    /// </summary>
    public static readonly ShipmentCarrier DpdLocal = new("DPD_LOCAL");

    /// <summary>
    /// Stallion Express.
    /// </summary>
    public static readonly ShipmentCarrier Stallionexpress = new("STALLIONEXPRESS");

    /// <summary>
    /// RaidereX.
    /// </summary>
    public static readonly ShipmentCarrier Raiderex = new("RAIDEREX");

    /// <summary>
    /// ShopfansRU LLC.
    /// </summary>
    public static readonly ShipmentCarrier Shopfans = new("SHOPFANS");

    /// <summary>
    /// Kyungdong Parcel.
    /// </summary>
    public static readonly ShipmentCarrier KyungdongParcel = new("KYUNGDONG_PARCEL");

    /// <summary>
    /// Champion Logistics.
    /// </summary>
    public static readonly ShipmentCarrier ChampionLogistics = new("CHAMPION_LOGISTICS");

    /// <summary>
    /// PICK UPP (Singapore).
    /// </summary>
    public static readonly ShipmentCarrier PickuppSgp = new("PICKUPP_SGP");

    /// <summary>
    /// Morning Express.
    /// </summary>
    public static readonly ShipmentCarrier MorningExpress = new("MORNING_EXPRESS");

    /// <summary>
    /// NACEX.
    /// </summary>
    public static readonly ShipmentCarrier Nacex = new("NACEX");

    /// <summary>
    /// SortHub courier.
    /// </summary>
    public static readonly ShipmentCarrier ThenileWebhook = new("THENILE_WEBHOOK");

    /// <summary>
    /// Holisol.
    /// </summary>
    public static readonly ShipmentCarrier Holisol = new("HOLISOL");

    /// <summary>
    /// LBC EXPRESS INC..
    /// </summary>
    public static readonly ShipmentCarrier LbcexpressFtp = new("LBCEXPRESS_FTP");

    /// <summary>
    /// KURASI.
    /// </summary>
    public static readonly ShipmentCarrier Kurasi = new("KURASI");

    /// <summary>
    /// USF Reddaway.
    /// </summary>
    public static readonly ShipmentCarrier UsfReddaway = new("USF_REDDAWAY");

    /// <summary>
    /// APG eCommerce Solutions.
    /// </summary>
    public static readonly ShipmentCarrier Apg = new("APG");

    /// <summary>
    /// BoxC courier.
    /// </summary>
    public static readonly ShipmentCarrier CnBoxc = new("CN_BOXC");

    /// <summary>
    /// ECOSCOOTING.
    /// </summary>
    public static readonly ShipmentCarrier Ecoscooting = new("ECOSCOOTING");

    /// <summary>
    /// Mainway.
    /// </summary>
    public static readonly ShipmentCarrier Mainway = new("MAINWAY");

    /// <summary>
    /// Paperfly Private Limited.
    /// </summary>
    public static readonly ShipmentCarrier Paperfly = new("PAPERFLY");

    /// <summary>
    /// Hound Express.
    /// </summary>
    public static readonly ShipmentCarrier Houndexpress = new("HOUNDEXPRESS");

    /// <summary>
    /// Boxberry courier.
    /// </summary>
    public static readonly ShipmentCarrier BoxBerry = new("BOX_BERRY");

    /// <summary>
    /// EP-Box courier.
    /// </summary>
    public static readonly ShipmentCarrier EpBox = new("EP_BOX");

    /// <summary>
    /// Plus UK Logistics.
    /// </summary>
    public static readonly ShipmentCarrier PlusLogUk = new("PLUS_LOG_UK");

    /// <summary>
    /// Fulfilla.
    /// </summary>
    public static readonly ShipmentCarrier Fulfilla = new("FULFILLA");

    /// <summary>
    /// ASE KARGO.
    /// </summary>
    public static readonly ShipmentCarrier Ase = new("ASE");

    /// <summary>
    /// MailPlus.
    /// </summary>
    public static readonly ShipmentCarrier MailPlus = new("MAIL_PLUS");

    /// <summary>
    /// XPO logistics.
    /// </summary>
    public static readonly ShipmentCarrier XpoLogistics = new("XPO_LOGISTICS");

    /// <summary>
    /// wnDirect.
    /// </summary>
    public static readonly ShipmentCarrier Wndirect = new("WNDIRECT");

    /// <summary>
    /// Cloudwish Asia.
    /// </summary>
    public static readonly ShipmentCarrier CloudwishAsia = new("CLOUDWISH_ASIA");

    /// <summary>
    /// Zeleris.
    /// </summary>
    public static readonly ShipmentCarrier Zeleris = new("ZELERIS");

    /// <summary>
    /// Gio Express.
    /// </summary>
    public static readonly ShipmentCarrier GioExpress = new("GIO_EXPRESS");

    /// <summary>
    /// OCS WORLDWIDE.
    /// </summary>
    public static readonly ShipmentCarrier OcsWorldwide = new("OCS_WORLDWIDE");

    /// <summary>
    /// ARK Logistics.
    /// </summary>
    public static readonly ShipmentCarrier ArkLogistics = new("ARK_LOGISTICS");

    /// <summary>
    /// Aquiline.
    /// </summary>
    public static readonly ShipmentCarrier Aquiline = new("AQUILINE");

    /// <summary>
    /// Pilot Freight Services.
    /// </summary>
    public static readonly ShipmentCarrier PilotFreight = new("PILOT_FREIGHT");

    /// <summary>
    /// Qwintry Logistics.
    /// </summary>
    public static readonly ShipmentCarrier Qwintry = new("QWINTRY");

    /// <summary>
    /// Danske Fragtaend.
    /// </summary>
    public static readonly ShipmentCarrier DanskeFragt = new("DANSKE_FRAGT");

    /// <summary>
    /// Carriers courier.
    /// </summary>
    public static readonly ShipmentCarrier Carriers = new("CARRIERS");

    /// <summary>
    /// Rivo (Air canada).
    /// </summary>
    public static readonly ShipmentCarrier AirCanadaGlobal = new("AIR_CANADA_GLOBAL");

    /// <summary>
    /// PRESIDENT TRANSNET CORP.
    /// </summary>
    public static readonly ShipmentCarrier PresidentTrans = new("PRESIDENT_TRANS");

    /// <summary>
    /// STEP FORWARD FREIGHT SERVICE CO LTD.
    /// </summary>
    public static readonly ShipmentCarrier Stepforwardfs = new("STEPFORWARDFS");

    /// <summary>
    /// Skynet UK.
    /// </summary>
    public static readonly ShipmentCarrier SkynetUk = new("SKYNET_UK");

    /// <summary>
    /// PITT OHIO.
    /// </summary>
    public static readonly ShipmentCarrier Pittohio = new("PITTOHIO");

    /// <summary>
    /// Correos Express.
    /// </summary>
    public static readonly ShipmentCarrier CorreosExpress = new("CORREOS_EXPRESS");

    /// <summary>
    /// RL Carriers.
    /// </summary>
    public static readonly ShipmentCarrier RlUs = new("RL_US");

    /// <summary>
    /// Destiny Transportation.
    /// </summary>
    public static readonly ShipmentCarrier Destiny = new("DESTINY");

    /// <summary>
    /// Yodel (www.yodel.co.uk).
    /// </summary>
    public static readonly ShipmentCarrier UkYodel = new("UK_YODEL");

    /// <summary>
    /// CometTech.
    /// </summary>
    public static readonly ShipmentCarrier CometTech = new("COMET_TECH");

    /// <summary>
    /// DHL Parcel Russia.
    /// </summary>
    public static readonly ShipmentCarrier DhlParcelRu = new("DHL_PARCEL_RU");

    /// <summary>
    /// TNT Reference.
    /// </summary>
    public static readonly ShipmentCarrier TntRefr = new("TNT_REFR");

    /// <summary>
    /// Shree Anjani Courier.
    /// </summary>
    public static readonly ShipmentCarrier ShreeAnjaniCourier = new("SHREE_ANJANI_COURIER");

    /// <summary>
    /// Mikropakket Belgium.
    /// </summary>
    public static readonly ShipmentCarrier MikropakketBe = new("MIKROPAKKET_BE");

    /// <summary>
    /// RETS express.
    /// </summary>
    public static readonly ShipmentCarrier EtsExpress = new("ETS_EXPRESS");

    /// <summary>
    /// Colis Privé.
    /// </summary>
    public static readonly ShipmentCarrier ColisPrive = new("COLIS_PRIVE");

    /// <summary>
    /// Yunda Express.
    /// </summary>
    public static readonly ShipmentCarrier CnYunda = new("CN_YUNDA");

    /// <summary>
    /// AAA Cooper.
    /// </summary>
    public static readonly ShipmentCarrier AaaCooper = new("AAA_COOPER");

    /// <summary>
    /// Rocket Parcel International.
    /// </summary>
    public static readonly ShipmentCarrier RocketParcel = new("ROCKET_PARCEL");

    /// <summary>
    /// 360 Lion Express.
    /// </summary>
    public static readonly ShipmentCarrier _360Lion = new("_360LION");

    /// <summary>
    /// PANDU.
    /// </summary>
    public static readonly ShipmentCarrier Pandu = new("PANDU");

    /// <summary>
    /// PROFESSIONAL COURIERS.
    /// </summary>
    public static readonly ShipmentCarrier ProfessionalCouriers = new("PROFESSIONAL_COURIERS");

    /// <summary>
    /// FLYTEXPRESS.
    /// </summary>
    public static readonly ShipmentCarrier Flytexpress = new("FLYTEXPRESS");

    /// <summary>
    /// LOGISTICSWORLDWIDE MY.
    /// </summary>
    public static readonly ShipmentCarrier LogisticsworldwideMy = new("LOGISTICSWORLDWIDE_MY");

    /// <summary>
    /// CORREOS DE ESPANA.
    /// </summary>
    public static readonly ShipmentCarrier CorreosDeEspana = new("CORREOS_DE_ESPANA");

    /// <summary>
    /// IMX.
    /// </summary>
    public static readonly ShipmentCarrier Imx = new("IMX");

    /// <summary>
    /// FOUR PX EXPRESS.
    /// </summary>
    public static readonly ShipmentCarrier FourPxExpress = new("FOUR_PX_EXPRESS");

    /// <summary>
    /// XPRESSBEES.
    /// </summary>
    public static readonly ShipmentCarrier Xpressbees = new("XPRESSBEES");

    /// <summary>
    /// pickupp_vnm.
    /// </summary>
    public static readonly ShipmentCarrier PickuppVnm = new("PICKUPP_VNM");

    /// <summary>
    /// startrack_express.
    /// </summary>
    public static readonly ShipmentCarrier StartrackExpress = new("STARTRACK_EXPRESS");

    /// <summary>
    /// fr_colissimo.
    /// </summary>
    public static readonly ShipmentCarrier FrColissimo = new("FR_COLISSIMO");

    /// <summary>
    /// nacex_spain_reference.
    /// </summary>
    public static readonly ShipmentCarrier NacexSpainReference = new("NACEX_SPAIN_REFERENCE");

    /// <summary>
    /// dhl_supply_chain_au.
    /// </summary>
    public static readonly ShipmentCarrier DhlSupplyChainAu = new("DHL_SUPPLY_CHAIN_AU");

    /// <summary>
    /// Eshipping.
    /// </summary>
    public static readonly ShipmentCarrier Eshipping = new("ESHIPPING");

    /// <summary>
    /// SHREE TIRUPATI COURIER SERVICES PVT. LTD..
    /// </summary>
    public static readonly ShipmentCarrier Shreetirupati = new("SHREETIRUPATI");

    /// <summary>
    /// HX Express.
    /// </summary>
    public static readonly ShipmentCarrier HxExpress = new("HX_EXPRESS");

    /// <summary>
    /// INDOPAKET.
    /// </summary>
    public static readonly ShipmentCarrier Indopaket = new("INDOPAKET");

    /// <summary>
    /// 17 Post Service.
    /// </summary>
    public static readonly ShipmentCarrier Cn17Post = new("CN_17POST");

    /// <summary>
    /// K1 Express.
    /// </summary>
    public static readonly ShipmentCarrier K1Express = new("K1_EXPRESS");

    /// <summary>
    /// CJ GLS.
    /// </summary>
    public static readonly ShipmentCarrier CjGls = new("CJ_GLS");

    /// <summary>
    /// GDEX courier.
    /// </summary>
    public static readonly ShipmentCarrier MysGdex = new("MYS_GDEX");

    /// <summary>
    /// Nationex courier.
    /// </summary>
    public static readonly ShipmentCarrier Nationex = new("NATIONEX");

    /// <summary>
    /// Anjun couriers.
    /// </summary>
    public static readonly ShipmentCarrier Anjun = new("ANJUN");

    /// <summary>
    /// FarGood.
    /// </summary>
    public static readonly ShipmentCarrier Fargood = new("FARGOOD");

    /// <summary>
    /// SMG Direct.
    /// </summary>
    public static readonly ShipmentCarrier SmgExpress = new("SMG_EXPRESS");

    /// <summary>
    /// RZY Express.
    /// </summary>
    public static readonly ShipmentCarrier Rzyexpress = new("RZYEXPRESS");

    /// <summary>
    /// Southeastern Freight Lines.
    /// </summary>
    public static readonly ShipmentCarrier Sefl = new("SEFL");

    /// <summary>
    /// TNT-Click Italy.
    /// </summary>
    public static readonly ShipmentCarrier TntClickIt = new("TNT_CLICK_IT");

    /// <summary>
    /// Haidaibao.
    /// </summary>
    public static readonly ShipmentCarrier Hdb = new("HDB");

    /// <summary>
    /// Hipshipper.
    /// </summary>
    public static readonly ShipmentCarrier Hipshipper = new("HIPSHIPPER");

    /// <summary>
    /// RPX Logistics.
    /// </summary>
    public static readonly ShipmentCarrier Rpxlogistics = new("RPXLOGISTICS");

    /// <summary>
    /// Kuehne + Nagel.
    /// </summary>
    public static readonly ShipmentCarrier Kuehne = new("KUEHNE");

    /// <summary>
    /// Nexive (TNT Post Italy).
    /// </summary>
    public static readonly ShipmentCarrier ItNexive = new("IT_NEXIVE");

    /// <summary>
    /// PTS courier.
    /// </summary>
    public static readonly ShipmentCarrier Pts = new("PTS");

    /// <summary>
    /// Swiss Post FTP.
    /// </summary>
    public static readonly ShipmentCarrier SwissPostFtp = new("SWISS_POST_FTP");

    /// <summary>
    /// Fastrak Services.
    /// </summary>
    public static readonly ShipmentCarrier FastrkServ = new("FASTRK_SERV");

    /// <summary>
    /// 4-72 Entregando.
    /// </summary>
    public static readonly ShipmentCarrier _472 = new("_4_72");

    /// <summary>
    /// YRC courier.
    /// </summary>
    public static readonly ShipmentCarrier UsYrc = new("US_YRC");

    /// <summary>
    /// PostNL International 3S.
    /// </summary>
    public static readonly ShipmentCarrier PostnlIntl3S = new("POSTNL_INTL_3S");

    /// <summary>
    /// Yilian (Elian) Supply Chain.
    /// </summary>
    public static readonly ShipmentCarrier ElianPost = new("ELIAN_POST");

    /// <summary>
    /// Cubyn.
    /// </summary>
    public static readonly ShipmentCarrier Cubyn = new("CUBYN");

    /// <summary>
    /// Saudi Post.
    /// </summary>
    public static readonly ShipmentCarrier SauSaudiPost = new("SAU_SAUDI_POST");

    /// <summary>
    /// ABX Express.
    /// </summary>
    public static readonly ShipmentCarrier AbxexpressMy = new("ABXEXPRESS_MY");

    /// <summary>
    /// HUAHANG EXPRESS.
    /// </summary>
    public static readonly ShipmentCarrier HuahanExpress = new("HUAHAN_EXPRESS");

    /// <summary>
    /// Eshun international Logistic.
    /// </summary>
    public static readonly ShipmentCarrier ZesExpress = new("ZES_EXPRESS");

    /// <summary>
    /// ZeptoExpress.
    /// </summary>
    public static readonly ShipmentCarrier ZeptoExpress = new("ZEPTO_EXPRESS");

    /// <summary>
    /// Skynet World Wide Express South Africa.
    /// </summary>
    public static readonly ShipmentCarrier SkynetZa = new("SKYNET_ZA");

    /// <summary>
    /// Zeek2Door.
    /// </summary>
    public static readonly ShipmentCarrier Zeek2Door = new("ZEEK_2_DOOR");

    /// <summary>
    /// Blink.
    /// </summary>
    public static readonly ShipmentCarrier Blinklastmile = new("BLINKLASTMILE");

    /// <summary>
    /// UkrPoshta.
    /// </summary>
    public static readonly ShipmentCarrier PostaUkr = new("POSTA_UKR");

    /// <summary>
    /// C.H. Robinson Worldwide.
    /// </summary>
    public static readonly ShipmentCarrier Chrobinson = new("CHROBINSON");

    /// <summary>
    /// Post56.
    /// </summary>
    public static readonly ShipmentCarrier CnPost56 = new("CN_POST56");

    /// <summary>
    /// Courant Plus.
    /// </summary>
    public static readonly ShipmentCarrier CourantPlus = new("COURANT_PLUS");

    /// <summary>
    /// Scudex Express.
    /// </summary>
    public static readonly ShipmentCarrier ScudexExpress = new("SCUDEX_EXPRESS");

    /// <summary>
    /// ShipEntegra.
    /// </summary>
    public static readonly ShipmentCarrier Shipentegra = new("SHIPENTEGRA");

    /// <summary>
    /// B2C courier Europe.
    /// </summary>
    public static readonly ShipmentCarrier BTwoCEurope = new("B_TWO_C_EUROPE");

    /// <summary>
    /// Cope Sensitive Freight.
    /// </summary>
    public static readonly ShipmentCarrier Cope = new("COPE");

    /// <summary>
    /// Gati-KWE.
    /// </summary>
    public static readonly ShipmentCarrier IndGati = new("IND_GATI");

    /// <summary>
    /// WishPost.
    /// </summary>
    public static readonly ShipmentCarrier CnWishpost = new("CN_WISHPOST");

    /// <summary>
    /// NACEX Spain.
    /// </summary>
    public static readonly ShipmentCarrier NacexEs = new("NACEX_ES");

    /// <summary>
    /// TAQBIN Hong Kong.
    /// </summary>
    public static readonly ShipmentCarrier TaqbinHk = new("TAQBIN_HK");

    /// <summary>
    /// GlobalTranz.
    /// </summary>
    public static readonly ShipmentCarrier Globaltranz = new("GLOBALTRANZ");

    /// <summary>
    /// Qingdao HKD International Logistics.
    /// </summary>
    public static readonly ShipmentCarrier Hkd = new("HKD");

    /// <summary>
    /// BJS Distribution courier.
    /// </summary>
    public static readonly ShipmentCarrier Bjshomedelivery = new("BJSHOMEDELIVERY");

    /// <summary>
    /// Omniva.
    /// </summary>
    public static readonly ShipmentCarrier Omniva = new("OMNIVA");

    /// <summary>
    /// Sutton Transport.
    /// </summary>
    public static readonly ShipmentCarrier Sutton = new("SUTTON");

    /// <summary>
    /// Panther Reference.
    /// </summary>
    public static readonly ShipmentCarrier PantherReference = new("PANTHER_REFERENCE");

    /// <summary>
    /// SFC Service.
    /// </summary>
    public static readonly ShipmentCarrier Sfcservice = new("SFCSERVICE");

    /// <summary>
    /// LTL COURIER.
    /// </summary>
    public static readonly ShipmentCarrier Ltl = new("LTL");

    /// <summary>
    /// Park N Parcel.
    /// </summary>
    public static readonly ShipmentCarrier Parknparcel = new("PARKNPARCEL");

    /// <summary>
    /// Spring GDS.
    /// </summary>
    public static readonly ShipmentCarrier SpringGds = new("SPRING_GDS");

    /// <summary>
    /// ECexpress.
    /// </summary>
    public static readonly ShipmentCarrier Ecexpress = new("ECEXPRESS");

    /// <summary>
    /// Interparcel Australia.
    /// </summary>
    public static readonly ShipmentCarrier InterparcelAu = new("INTERPARCEL_AU");

    /// <summary>
    /// Agility.
    /// </summary>
    public static readonly ShipmentCarrier Agility = new("AGILITY");

    /// <summary>
    /// XL Express.
    /// </summary>
    public static readonly ShipmentCarrier XlExpress = new("XL_EXPRESS");

    /// <summary>
    /// Ader couriers.
    /// </summary>
    public static readonly ShipmentCarrier Aderonline = new("ADERONLINE");

    /// <summary>
    /// Direct Couriers.
    /// </summary>
    public static readonly ShipmentCarrier Directcouriers = new("DIRECTCOURIERS");

    /// <summary>
    /// Planzer Group.
    /// </summary>
    public static readonly ShipmentCarrier Planzer = new("PLANZER");

    /// <summary>
    /// Sending Transporte Urgente y Comunicacion.
    /// </summary>
    public static readonly ShipmentCarrier Sending = new("SENDING");

    /// <summary>
    /// Ninjavan Webhook.
    /// </summary>
    public static readonly ShipmentCarrier NinjavanWb = new("NINJAVAN_WB");

    /// <summary>
    /// Nationwide Express Courier Services Bhd (www.nationwide.com.my).
    /// </summary>
    public static readonly ShipmentCarrier NationwideMy = new("NATIONWIDE_MY");

    /// <summary>
    /// Sendit.
    /// </summary>
    public static readonly ShipmentCarrier Sendit = new("SENDIT");

    /// <summary>
    /// Arrow XL.
    /// </summary>
    public static readonly ShipmentCarrier GbArrow = new("GB_ARROW");

    /// <summary>
    /// GoJavas.
    /// </summary>
    public static readonly ShipmentCarrier IndGojavas = new("IND_GOJAVAS");

    /// <summary>
    /// Korea Post.
    /// </summary>
    public static readonly ShipmentCarrier Kpost = new("KPOST");

    /// <summary>
    /// DHL Freight.
    /// </summary>
    public static readonly ShipmentCarrier DhlFreight = new("DHL_FREIGHT");

    /// <summary>
    /// Bluecare Express Ltd.
    /// </summary>
    public static readonly ShipmentCarrier Bluecare = new("BLUECARE");

    /// <summary>
    /// jindouyun courier.
    /// </summary>
    public static readonly ShipmentCarrier Jindouyun = new("JINDOUYUN");

    /// <summary>
    /// Trackon Couriers Pvt. Ltd.
    /// </summary>
    public static readonly ShipmentCarrier Trackon = new("TRACKON");

    /// <summary>
    /// Tuffnells Parcels Express.
    /// </summary>
    public static readonly ShipmentCarrier GbTuffnells = new("GB_TUFFNELLS");

    /// <summary>
    /// TRUMPCARD LLC.
    /// </summary>
    public static readonly ShipmentCarrier Trumpcard = new("TRUMPCARD");

    /// <summary>
    /// eTotal Solution Limited.
    /// </summary>
    public static readonly ShipmentCarrier Etotal = new("ETOTAL");

    /// <summary>
    /// Zeek courier.
    /// </summary>
    public static readonly ShipmentCarrier SfplusWebhook = new("SFPLUS_WEBHOOK");

    /// <summary>
    /// SEKO Logistics.
    /// </summary>
    public static readonly ShipmentCarrier Sekologistics = new("SEKOLOGISTICS");

    /// <summary>
    /// Hermes Einrichtungs Service GmbH &amp; Co. KG.
    /// </summary>
    public static readonly ShipmentCarrier Hermes2MannHandling = new("HERMES_2MANN_HANDLING");

    /// <summary>
    /// DPD Local reference.
    /// </summary>
    public static readonly ShipmentCarrier DpdLocalRef = new("DPD_LOCAL_REF");

    /// <summary>
    /// United Delivery Service.
    /// </summary>
    public static readonly ShipmentCarrier Uds = new("UDS");

    /// <summary>
    /// Specialised Freight.
    /// </summary>
    public static readonly ShipmentCarrier ZaSpecialisedFreight = new("ZA_SPECIALISED_FREIGHT");

    /// <summary>
    /// Kerry Express Thailand.
    /// </summary>
    public static readonly ShipmentCarrier ThaKerry = new("THA_KERRY");

    /// <summary>
    /// SEUR International.
    /// </summary>
    public static readonly ShipmentCarrier PrtIntSeur = new("PRT_INT_SEUR");

    /// <summary>
    /// Correios Brazil.
    /// </summary>
    public static readonly ShipmentCarrier BraCorreios = new("BRA_CORREIOS");

    /// <summary>
    /// New Zealand Post.
    /// </summary>
    public static readonly ShipmentCarrier NzNzPost = new("NZ_NZ_POST");

    /// <summary>
    /// Equick China.
    /// </summary>
    public static readonly ShipmentCarrier CnEquick = new("CN_EQUICK");

    /// <summary>
    /// Malaysia Post EMS / Pos Laju.
    /// </summary>
    public static readonly ShipmentCarrier MysEms = new("MYS_EMS");

    /// <summary>
    /// Norsk Global.
    /// </summary>
    public static readonly ShipmentCarrier GbNorsk = new("GB_NORSK");

    /// <summary>
    /// MRW spain.
    /// </summary>
    public static readonly ShipmentCarrier EspMrw = new("ESP_MRW");

    /// <summary>
    /// Packlink.
    /// </summary>
    public static readonly ShipmentCarrier EspPacklink = new("ESP_PACKLINK");

    /// <summary>
    /// Kangaroo Worldwide Express.
    /// </summary>
    public static readonly ShipmentCarrier KangarooMy = new("KANGAROO_MY");

    /// <summary>
    /// RPX Online.
    /// </summary>
    public static readonly ShipmentCarrier Rpx = new("RPX");

    /// <summary>
    /// XDP Express Reference.
    /// </summary>
    public static readonly ShipmentCarrier XdpUkReference = new("XDP_UK_REFERENCE");

    /// <summary>
    /// ninja van (www.ninjavan.co).
    /// </summary>
    public static readonly ShipmentCarrier NinjavanMy = new("NINJAVAN_MY");

    /// <summary>
    /// Adicional Logistics.
    /// </summary>
    public static readonly ShipmentCarrier Adicional = new("ADICIONAL");

    /// <summary>
    /// Red Carpet Logistics.
    /// </summary>
    public static readonly ShipmentCarrier Roadbull = new("ROADBULL");

    /// <summary>
    /// Yakit courier.
    /// </summary>
    public static readonly ShipmentCarrier Yakit = new("YAKIT");

    /// <summary>
    /// MailAmericas.
    /// </summary>
    public static readonly ShipmentCarrier Mailamericas = new("MAILAMERICAS");

    /// <summary>
    /// Mikropakket.
    /// </summary>
    public static readonly ShipmentCarrier Mikropakket = new("MIKROPAKKET");

    /// <summary>
    /// Dynamic Logistics.
    /// </summary>
    public static readonly ShipmentCarrier Dynalogic = new("DYNALOGIC");

    /// <summary>
    /// DHL Spain(www.dhl.com).
    /// </summary>
    public static readonly ShipmentCarrier DhlEs = new("DHL_ES");

    /// <summary>
    /// DHL Parcel NL.
    /// </summary>
    public static readonly ShipmentCarrier DhlParcelNl = new("DHL_PARCEL_NL");

    /// <summary>
    /// DHL Global Mail Asia (www.dhl.com).
    /// </summary>
    public static readonly ShipmentCarrier DhlGlobalMailAsia = new("DHL_GLOBAL_MAIL_ASIA");

    /// <summary>
    /// Dawn Wing.
    /// </summary>
    public static readonly ShipmentCarrier DawnWing = new("DAWN_WING");

    /// <summary>
    /// Geniki Taxydromiki.
    /// </summary>
    public static readonly ShipmentCarrier GenikiGr = new("GENIKI_GR");

    /// <summary>
    /// hermesworld_uk.
    /// </summary>
    public static readonly ShipmentCarrier HermesworldUk = new("HERMESWORLD_UK");

    /// <summary>
    /// Alphafast (www.alphafast.com).
    /// </summary>
    public static readonly ShipmentCarrier Alphafast = new("ALPHAFAST");

    /// <summary>
    /// buylogic.
    /// </summary>
    public static readonly ShipmentCarrier Buylogic = new("BUYLOGIC");

    /// <summary>
    /// Ekart logistics (ekartlogistics.com).
    /// </summary>
    public static readonly ShipmentCarrier Ekart = new("EKART");

    /// <summary>
    /// mexico senda express.
    /// </summary>
    public static readonly ShipmentCarrier MexSenda = new("MEX_SENDA");

    /// <summary>
    /// SFC.
    /// </summary>
    public static readonly ShipmentCarrier SfcLogistics = new("SFC_LOGISTICS");

    /// <summary>
    /// Posta Serbia.
    /// </summary>
    public static readonly ShipmentCarrier PostSerbia = new("POST_SERBIA");

    /// <summary>
    /// Delhivery India.
    /// </summary>
    public static readonly ShipmentCarrier IndDelhivery = new("IND_DELHIVERY");

    /// <summary>
    /// DPD Germany.
    /// </summary>
    public static readonly ShipmentCarrier DeDpdDelistrack = new("DE_DPD_DELISTRACK");

    /// <summary>
    /// RPD2man Deliveries.
    /// </summary>
    public static readonly ShipmentCarrier Rpd2Man = new("RPD2MAN");

    /// <summary>
    /// SF Express (www.sf-express.com).
    /// </summary>
    public static readonly ShipmentCarrier CnSfExpress = new("CN_SF_EXPRESS");

    /// <summary>
    /// Yanwen Logistics.
    /// </summary>
    public static readonly ShipmentCarrier Yanwen = new("YANWEN");

    /// <summary>
    /// Skynet Malaysia.
    /// </summary>
    public static readonly ShipmentCarrier MysSkynet = new("MYS_SKYNET");

    /// <summary>
    /// correos mexico.
    /// </summary>
    public static readonly ShipmentCarrier CorreosDeMexico = new("CORREOS_DE_MEXICO");

    /// <summary>
    /// CBL Logistica.
    /// </summary>
    public static readonly ShipmentCarrier CblLogistica = new("CBL_LOGISTICA");

    /// <summary>
    /// Estafeta (www.estafeta.com).
    /// </summary>
    public static readonly ShipmentCarrier MexEstafeta = new("MEX_ESTAFETA");

    /// <summary>
    /// Austrian Post (Registered).
    /// </summary>
    public static readonly ShipmentCarrier AuAustrianPost = new("AU_AUSTRIAN_POST");

    /// <summary>
    /// Rincos.
    /// </summary>
    public static readonly ShipmentCarrier Rincos = new("RINCOS");

    /// <summary>
    /// DHL Netherland.
    /// </summary>
    public static readonly ShipmentCarrier NldDhl = new("NLD_DHL");

    /// <summary>
    /// Russian post.
    /// </summary>
    public static readonly ShipmentCarrier RussianPost = new("RUSSIAN_POST");

    /// <summary>
    /// CouriersPlease (couriersplease.com.au).
    /// </summary>
    public static readonly ShipmentCarrier CouriersPlease = new("COURIERS_PLEASE");

    /// <summary>
    /// PostNord Logistics.
    /// </summary>
    public static readonly ShipmentCarrier PostnordLogistics = new("POSTNORD_LOGISTICS");

    /// <summary>
    /// Fedex.
    /// </summary>
    public static readonly ShipmentCarrier Fedex = new("FEDEX");

    /// <summary>
    /// DPE Express.
    /// </summary>
    public static readonly ShipmentCarrier DpeExpress = new("DPE_EXPRESS");

    /// <summary>
    /// DPD.
    /// </summary>
    public static readonly ShipmentCarrier Dpd = new("DPD");

    /// <summary>
    /// ADSone.
    /// </summary>
    public static readonly ShipmentCarrier Adsone = new("ADSONE");

    /// <summary>
    /// JNE Express (Jalur Nugraha Ekakurir).
    /// </summary>
    public static readonly ShipmentCarrier IdnJne = new("IDN_JNE");

    /// <summary>
    /// The Courier Guy.
    /// </summary>
    public static readonly ShipmentCarrier Thecourierguy = new("THECOURIERGUY");

    /// <summary>
    /// CNE Express.
    /// </summary>
    public static readonly ShipmentCarrier Cnexps = new("CNEXPS");

    /// <summary>
    /// Chronopost Portugal.
    /// </summary>
    public static readonly ShipmentCarrier PrtChronopost = new("PRT_CHRONOPOST");

    /// <summary>
    /// Landmark Global.
    /// </summary>
    public static readonly ShipmentCarrier LandmarkGlobal = new("LANDMARK_GLOBAL");

    /// <summary>
    /// DHL International.
    /// </summary>
    public static readonly ShipmentCarrier ItDhlEcommerce = new("IT_DHL_ECOMMERCE");

    /// <summary>
    /// NACEX Spain.
    /// </summary>
    public static readonly ShipmentCarrier EspNacex = new("ESP_NACEX");

    /// <summary>
    /// CTT Portugal.
    /// </summary>
    public static readonly ShipmentCarrier PrtCtt = new("PRT_CTT");

    /// <summary>
    /// Kiala.
    /// </summary>
    public static readonly ShipmentCarrier BeKiala = new("BE_KIALA");

    /// <summary>
    /// Asendia UK.
    /// </summary>
    public static readonly ShipmentCarrier AsendiaUk = new("ASENDIA_UK");

    /// <summary>
    /// TNT global.
    /// </summary>
    public static readonly ShipmentCarrier GlobalTnt = new("GLOBAL_TNT");

    /// <summary>
    /// Iceland Post.
    /// </summary>
    public static readonly ShipmentCarrier PosturIs = new("POSTUR_IS");

    /// <summary>
    /// eParcel Korea.
    /// </summary>
    public static readonly ShipmentCarrier EparcelKr = new("EPARCEL_KR");

    /// <summary>
    /// InPost Paczkomaty.
    /// </summary>
    public static readonly ShipmentCarrier InpostPaczkomaty = new("INPOST_PACZKOMATY");

    /// <summary>
    /// Poste italiane (www.poste.it).
    /// </summary>
    public static readonly ShipmentCarrier ItPosteItalia = new("IT_POSTE_ITALIA");

    /// <summary>
    /// Bpost (www.bpost.be).
    /// </summary>
    public static readonly ShipmentCarrier BeBpost = new("BE_BPOST");

    /// <summary>
    /// Poczta Polska (www.poczta-polska.pl).
    /// </summary>
    public static readonly ShipmentCarrier PlPocztaPolska = new("PL_POCZTA_POLSKA");

    /// <summary>
    /// Malaysia Post.
    /// </summary>
    public static readonly ShipmentCarrier MysMysPost = new("MYS_MYS_POST");

    /// <summary>
    /// Singapore Post.
    /// </summary>
    public static readonly ShipmentCarrier SgSgPost = new("SG_SG_POST");

    /// <summary>
    /// Thailand Post (www.thailandpost.co.th).
    /// </summary>
    public static readonly ShipmentCarrier ThaThailandPost = new("THA_THAILAND_POST");

    /// <summary>
    /// LexShip.
    /// </summary>
    public static readonly ShipmentCarrier Lexship = new("LEXSHIP");

    /// <summary>
    /// Fastway New Zealand.
    /// </summary>
    public static readonly ShipmentCarrier FastwayNz = new("FASTWAY_NZ");

    /// <summary>
    /// DHL Supply Chain Australia.
    /// </summary>
    public static readonly ShipmentCarrier DhlAu = new("DHL_AU");

    /// <summary>
    /// Cosmetics Now.
    /// </summary>
    public static readonly ShipmentCarrier Costmeticsnow = new("COSTMETICSNOW");

    /// <summary>
    /// PFL.
    /// </summary>
    public static readonly ShipmentCarrier Pflogistics = new("PFLOGISTICS");

    /// <summary>
    /// Loomis Express.
    /// </summary>
    public static readonly ShipmentCarrier LoomisExpress = new("LOOMIS_EXPRESS");

    /// <summary>
    /// GLS Italy.
    /// </summary>
    public static readonly ShipmentCarrier GlsItaly = new("GLS_ITALY");

    /// <summary>
    /// Line Clear Express &amp; Logistics Sdn Bhd.
    /// </summary>
    public static readonly ShipmentCarrier Line = new("LINE");

    /// <summary>
    /// Gel Express Logistik.
    /// </summary>
    public static readonly ShipmentCarrier GelExpress = new("GEL_EXPRESS");

    /// <summary>
    /// Huodull.
    /// </summary>
    public static readonly ShipmentCarrier Huodull = new("HUODULL");

    /// <summary>
    /// Ninja van Singapore.
    /// </summary>
    public static readonly ShipmentCarrier NinjavanSg = new("NINJAVAN_SG");

    /// <summary>
    /// Janio Asia.
    /// </summary>
    public static readonly ShipmentCarrier Janio = new("JANIO");

    /// <summary>
    /// AO Logistics.
    /// </summary>
    public static readonly ShipmentCarrier AoCourier = new("AO_COURIER");

    /// <summary>
    /// BRT Bartolini(Sender Reference).
    /// </summary>
    public static readonly ShipmentCarrier BrtItSenderRef = new("BRT_IT_SENDER_REF");

    /// <summary>
    /// SAILPOST.
    /// </summary>
    public static readonly ShipmentCarrier Sailpost = new("SAILPOST");

    /// <summary>
    /// Lalamove.
    /// </summary>
    public static readonly ShipmentCarrier Lalamove = new("LALAMOVE");

    /// <summary>
    /// NEW ZEALAND COURIERS.
    /// </summary>
    public static readonly ShipmentCarrier NewzealandCouriers = new("NEWZEALAND_COURIERS");

    /// <summary>
    /// Etomars.
    /// </summary>
    public static readonly ShipmentCarrier Etomars = new("ETOMARS");

    /// <summary>
    /// VIR Transport.
    /// </summary>
    public static readonly ShipmentCarrier Virtransport = new("VIRTRANSPORT");

    /// <summary>
    /// Wizmo.
    /// </summary>
    public static readonly ShipmentCarrier Wizmo = new("WIZMO");

    /// <summary>
    /// Palletways.
    /// </summary>
    public static readonly ShipmentCarrier Palletways = new("PALLETWAYS");

    /// <summary>
    /// i-dika.
    /// </summary>
    public static readonly ShipmentCarrier IDika = new("I_DIKA");

    /// <summary>
    /// CFL Logistics.
    /// </summary>
    public static readonly ShipmentCarrier CflLogistics = new("CFL_LOGISTICS");

    /// <summary>
    /// GEM Worldwide.
    /// </summary>
    public static readonly ShipmentCarrier Gemworldwide = new("GEMWORLDWIDE");

    /// <summary>
    /// Tai Wan Global Business.
    /// </summary>
    public static readonly ShipmentCarrier GlobalExpress = new("GLOBAL_EXPRESS");

    /// <summary>
    /// Transgroup courier.
    /// </summary>
    public static readonly ShipmentCarrier LogistyxTransgroup = new("LOGISTYX_TRANSGROUP");

    /// <summary>
    /// West Bank Courier.
    /// </summary>
    public static readonly ShipmentCarrier WestbankCourier = new("WESTBANK_COURIER");

    /// <summary>
    /// Arco Spedizioni SP.
    /// </summary>
    public static readonly ShipmentCarrier ArcoSpedizioni = new("ARCO_SPEDIZIONI");

    /// <summary>
    /// YDH express.
    /// </summary>
    public static readonly ShipmentCarrier YdhExpress = new("YDH_EXPRESS");

    /// <summary>
    /// Parcelink Logistics.
    /// </summary>
    public static readonly ShipmentCarrier Parcelinklogistics = new("PARCELINKLOGISTICS");

    /// <summary>
    /// CND Express.
    /// </summary>
    public static readonly ShipmentCarrier Cndexpress = new("CNDEXPRESS");

    /// <summary>
    /// NOX NightTimeExpress.
    /// </summary>
    public static readonly ShipmentCarrier NoxNightTimeExpress = new("NOX_NIGHT_TIME_EXPRESS");

    /// <summary>
    /// Aeronet couriers.
    /// </summary>
    public static readonly ShipmentCarrier Aeronet = new("AERONET");

    /// <summary>
    /// LTIAN EXP.
    /// </summary>
    public static readonly ShipmentCarrier Ltianexp = new("LTIANEXP");

    /// <summary>
    /// Integra2.
    /// </summary>
    public static readonly ShipmentCarrier Integra2Ftp = new("INTEGRA2_FTP");

    /// <summary>
    /// PARCEL ONE.
    /// </summary>
    public static readonly ShipmentCarrier Parcelone = new("PARCELONE");

    /// <summary>
    /// Innight Express Germany GmbH (nox NachtExpress).
    /// </summary>
    public static readonly ShipmentCarrier NoxNachtexpress = new("NOX_NACHTEXPRESS");

    /// <summary>
    /// China Post.
    /// </summary>
    public static readonly ShipmentCarrier CnChinaPostEms = new("CN_CHINA_POST_EMS");

    /// <summary>
    /// Chukou1.
    /// </summary>
    public static readonly ShipmentCarrier Chukou1 = new("CHUKOU1");

    /// <summary>
    /// GLS General Logistics Systems Slovakia s.r.o..
    /// </summary>
    public static readonly ShipmentCarrier GlsSlov = new("GLS_SLOV");

    /// <summary>
    /// OrangeDS (Orange Distribution Solutions Inc).
    /// </summary>
    public static readonly ShipmentCarrier OrangeDs = new("ORANGE_DS");

    /// <summary>
    /// Joom Logistics.
    /// </summary>
    public static readonly ShipmentCarrier JoomLogis = new("JOOM_LOGIS");

    /// <summary>
    /// StarTrack (startrack.com.au).
    /// </summary>
    public static readonly ShipmentCarrier AusStartrack = new("AUS_STARTRACK");

    /// <summary>
    /// dhl Global.
    /// </summary>
    public static readonly ShipmentCarrier Dhl = new("DHL");

    /// <summary>
    /// APC postal logistics germany.
    /// </summary>
    public static readonly ShipmentCarrier GbApc = new("GB_APC");

    /// <summary>
    /// Bonds Courier Service (bondscouriers.com.au).
    /// </summary>
    public static readonly ShipmentCarrier Bondscouriers = new("BONDSCOURIERS");

    /// <summary>
    /// Japan Post.
    /// </summary>
    public static readonly ShipmentCarrier JpnJapanPost = new("JPN_JAPAN_POST");

    /// <summary>
    /// United States Postal Service.
    /// </summary>
    public static readonly ShipmentCarrier Usps = new("USPS");

    /// <summary>
    /// WinIt.
    /// </summary>
    public static readonly ShipmentCarrier Winit = new("WINIT");

    /// <summary>
    /// OCA Argentina.
    /// </summary>
    public static readonly ShipmentCarrier ArgOca = new("ARG_OCA");

    /// <summary>
    /// Taiwan Post.
    /// </summary>
    public static readonly ShipmentCarrier TwTaiwanPost = new("TW_TAIWAN_POST");

    /// <summary>
    /// DMM Network.
    /// </summary>
    public static readonly ShipmentCarrier DmmNetwork = new("DMM_NETWORK");

    /// <summary>
    /// TNT Express.
    /// </summary>
    public static readonly ShipmentCarrier Tnt = new("TNT");

    /// <summary>
    /// BH Posta (www.posta.ba).
    /// </summary>
    public static readonly ShipmentCarrier BhPosta = new("BH_POSTA");

    /// <summary>
    /// Postnord sweden.
    /// </summary>
    public static readonly ShipmentCarrier SwePostnord = new("SWE_POSTNORD");

    /// <summary>
    /// Canada Post.
    /// </summary>
    public static readonly ShipmentCarrier CaCanadaPost = new("CA_CANADA_POST");

    /// <summary>
    /// Wiseloads.
    /// </summary>
    public static readonly ShipmentCarrier Wiseloads = new("WISELOADS");

    /// <summary>
    /// Asendia HonKong.
    /// </summary>
    public static readonly ShipmentCarrier AsendiaHk = new("ASENDIA_HK");

    /// <summary>
    /// GLS Netherland.
    /// </summary>
    public static readonly ShipmentCarrier NldGls = new("NLD_GLS");

    /// <summary>
    /// Redpack.
    /// </summary>
    public static readonly ShipmentCarrier MexRedpack = new("MEX_REDPACK");

    /// <summary>
    /// Jet-Ship Worldwide.
    /// </summary>
    public static readonly ShipmentCarrier JetShip = new("JET_SHIP");

    /// <summary>
    /// DHL Express.
    /// </summary>
    public static readonly ShipmentCarrier DeDhlExpress = new("DE_DHL_EXPRESS");

    /// <summary>
    /// Ninja van Thai.
    /// </summary>
    public static readonly ShipmentCarrier NinjavanThai = new("NINJAVAN_THAI");

    /// <summary>
    /// Raben Group.
    /// </summary>
    public static readonly ShipmentCarrier RabenGroup = new("RABEN_GROUP");

    /// <summary>
    /// ASM(GLS Spain).
    /// </summary>
    public static readonly ShipmentCarrier EspAsm = new("ESP_ASM");

    /// <summary>
    /// Hrvatska posta.
    /// </summary>
    public static readonly ShipmentCarrier HrvHrvatska = new("HRV_HRVATSKA");

    /// <summary>
    /// Estes Express Lines.
    /// </summary>
    public static readonly ShipmentCarrier GlobalEstes = new("GLOBAL_ESTES");

    /// <summary>
    /// Lietuvos pastas.
    /// </summary>
    public static readonly ShipmentCarrier LtuLietuvos = new("LTU_LIETUVOS");

    /// <summary>
    /// DHL Benelux.
    /// </summary>
    public static readonly ShipmentCarrier BelDhl = new("BEL_DHL");

    /// <summary>
    /// Australia Post.
    /// </summary>
    public static readonly ShipmentCarrier AuAuPost = new("AU_AU_POST");

    /// <summary>
    /// SPEEDEX couriers.
    /// </summary>
    public static readonly ShipmentCarrier Speedexcourier = new("SPEEDEXCOURIER");

    /// <summary>
    /// Colissimo.
    /// </summary>
    public static readonly ShipmentCarrier FrColis = new("FR_COLIS");

    /// <summary>
    /// Aramex.
    /// </summary>
    public static readonly ShipmentCarrier Aramex = new("ARAMEX");

    /// <summary>
    /// DPEX (www.dpex.com).
    /// </summary>
    public static readonly ShipmentCarrier Dpex = new("DPEX");

    /// <summary>
    /// Airpak Express.
    /// </summary>
    public static readonly ShipmentCarrier MysAirpak = new("MYS_AIRPAK");

    /// <summary>
    /// Cuckoo Express.
    /// </summary>
    public static readonly ShipmentCarrier Cuckooexpress = new("CUCKOOEXPRESS");

    /// <summary>
    /// DPD Poland.
    /// </summary>
    public static readonly ShipmentCarrier DpdPoland = new("DPD_POLAND");

    /// <summary>
    /// PostNL International.
    /// </summary>
    public static readonly ShipmentCarrier NldPostnl = new("NLD_POSTNL");

    /// <summary>
    /// Nim Express.
    /// </summary>
    public static readonly ShipmentCarrier NimExpress = new("NIM_EXPRESS");

    /// <summary>
    /// Quantium.
    /// </summary>
    public static readonly ShipmentCarrier Quantium = new("QUANTIUM");

    /// <summary>
    /// Sendle.
    /// </summary>
    public static readonly ShipmentCarrier Sendle = new("SENDLE");

    /// <summary>
    /// Redur Spain.
    /// </summary>
    public static readonly ShipmentCarrier EspRedur = new("ESP_REDUR");

    /// <summary>
    /// Matkahuolto.
    /// </summary>
    public static readonly ShipmentCarrier Matkahuolto = new("MATKAHUOLTO");

    /// <summary>
    /// Cpacket couriers.
    /// </summary>
    public static readonly ShipmentCarrier Cpacket = new("CPACKET");

    /// <summary>
    /// Posti courier.
    /// </summary>
    public static readonly ShipmentCarrier Posti = new("POSTI");

    /// <summary>
    /// Hunter Express.
    /// </summary>
    public static readonly ShipmentCarrier HunterExpress = new("HUNTER_EXPRESS");

    /// <summary>
    /// Choir Express Indonesia.
    /// </summary>
    public static readonly ShipmentCarrier ChoirExp = new("CHOIR_EXP");

    /// <summary>
    /// Legion Express.
    /// </summary>
    public static readonly ShipmentCarrier LegionExpress = new("LEGION_EXPRESS");

    /// <summary>
    /// austrian post.
    /// </summary>
    public static readonly ShipmentCarrier AustrianPostExpress = new("AUSTRIAN_POST_EXPRESS");

    /// <summary>
    /// Grupo ampm.
    /// </summary>
    public static readonly ShipmentCarrier Grupo = new("GRUPO");

    /// <summary>
    /// Post Roman (www.posta-romana.ro).
    /// </summary>
    public static readonly ShipmentCarrier PostaRo = new("POSTA_RO");

    /// <summary>
    /// Interparcel UK.
    /// </summary>
    public static readonly ShipmentCarrier InterparcelUk = new("INTERPARCEL_UK");

    /// <summary>
    /// ABF Freight.
    /// </summary>
    public static readonly ShipmentCarrier GlobalAbf = new("GLOBAL_ABF");

    /// <summary>
    /// Posten Norge (www.posten.no).
    /// </summary>
    public static readonly ShipmentCarrier PostenNorge = new("POSTEN_NORGE");

    /// <summary>
    /// Xpert Delivery.
    /// </summary>
    public static readonly ShipmentCarrier XpertDelivery = new("XPERT_DELIVERY");

    /// <summary>
    /// DHl (Reference number).
    /// </summary>
    public static readonly ShipmentCarrier DhlRefr = new("DHL_REFR");

    /// <summary>
    /// DHL HonKong.
    /// </summary>
    public static readonly ShipmentCarrier DhlHk = new("DHL_HK");

    /// <summary>
    /// SKYNET UAE.
    /// </summary>
    public static readonly ShipmentCarrier SkynetUae = new("SKYNET_UAE");

    /// <summary>
    /// Gojek.
    /// </summary>
    public static readonly ShipmentCarrier Gojek = new("GOJEK");

    /// <summary>
    /// Yodel International.
    /// </summary>
    public static readonly ShipmentCarrier YodelIntnl = new("YODEL_INTNL");

    /// <summary>
    /// Janco Ecommerce.
    /// </summary>
    public static readonly ShipmentCarrier Janco = new("JANCO");

    /// <summary>
    /// YTO Express.
    /// </summary>
    public static readonly ShipmentCarrier Yto = new("YTO");

    /// <summary>
    /// Wise Express.
    /// </summary>
    public static readonly ShipmentCarrier WiseExpress = new("WISE_EXPRESS");

    /// <summary>
    /// J&amp;T Express Vietnam.
    /// </summary>
    public static readonly ShipmentCarrier JtexpressVn = new("JTEXPRESS_VN");

    /// <summary>
    /// FedEx International MailService.
    /// </summary>
    public static readonly ShipmentCarrier FedexIntlMlserv = new("FEDEX_INTL_MLSERV");

    /// <summary>
    /// VAMOX.
    /// </summary>
    public static readonly ShipmentCarrier Vamox = new("VAMOX");

    /// <summary>
    /// AMS Group.
    /// </summary>
    public static readonly ShipmentCarrier AmsGrp = new("AMS_GRP");

    /// <summary>
    /// DHL Japan.
    /// </summary>
    public static readonly ShipmentCarrier DhlJp = new("DHL_JP");

    /// <summary>
    /// HR Parcel.
    /// </summary>
    public static readonly ShipmentCarrier Hrparcel = new("HRPARCEL");

    /// <summary>
    /// GESWL Express.
    /// </summary>
    public static readonly ShipmentCarrier Geswl = new("GESWL");

    /// <summary>
    /// Blue Star.
    /// </summary>
    public static readonly ShipmentCarrier Bluestar = new("BLUESTAR");

    /// <summary>
    /// CDEK TR.
    /// </summary>
    public static readonly ShipmentCarrier CdekTr = new("CDEK_TR");

    /// <summary>
    /// Innovel courier.
    /// </summary>
    public static readonly ShipmentCarrier Descartes = new("DESCARTES");

    /// <summary>
    /// Deltec Courier.
    /// </summary>
    public static readonly ShipmentCarrier DeltecUk = new("DELTEC_UK");

    /// <summary>
    /// DTDC express.
    /// </summary>
    public static readonly ShipmentCarrier DtdcExpress = new("DTDC_EXPRESS");

    /// <summary>
    /// tourline.
    /// </summary>
    public static readonly ShipmentCarrier Tourline = new("TOURLINE");

    /// <summary>
    /// B&amp;H Worldwide.
    /// </summary>
    public static readonly ShipmentCarrier BhWorldwide = new("BH_WORLDWIDE");

    /// <summary>
    /// OCS ANA Group.
    /// </summary>
    public static readonly ShipmentCarrier Ocs = new("OCS");

    /// <summary>
    /// yingnuo logistics.
    /// </summary>
    public static readonly ShipmentCarrier YingnuoLogistics = new("YINGNUO_LOGISTICS");

    /// <summary>
    /// United Parcel Service.
    /// </summary>
    public static readonly ShipmentCarrier Ups = new("UPS");

    /// <summary>
    /// Toll IPEC.
    /// </summary>
    public static readonly ShipmentCarrier Toll = new("TOLL");

    /// <summary>
    /// SEUR portugal.
    /// </summary>
    public static readonly ShipmentCarrier PrtSeur = new("PRT_SEUR");

    /// <summary>
    /// DTDC Australia.
    /// </summary>
    public static readonly ShipmentCarrier DtdcAu = new("DTDC_AU");

    /// <summary>
    /// Dynamic Logistics.
    /// </summary>
    public static readonly ShipmentCarrier ThaDynamicLogistics = new("THA_DYNAMIC_LOGISTICS");

    /// <summary>
    /// UBI Smart Parcel.
    /// </summary>
    public static readonly ShipmentCarrier UbiLogistics = new("UBI_LOGISTICS");

    /// <summary>
    /// FedEx Cross Border.
    /// </summary>
    public static readonly ShipmentCarrier FedexCrossborder = new("FEDEX_CROSSBORDER");

    /// <summary>
    /// A1Post.
    /// </summary>
    public static readonly ShipmentCarrier A1Post = new("A1POST");

    /// <summary>
    /// Tazmanian Freight Systems.
    /// </summary>
    public static readonly ShipmentCarrier TazmanianFreight = new("TAZMANIAN_FREIGHT");

    /// <summary>
    /// CJ International malaysia.
    /// </summary>
    public static readonly ShipmentCarrier CjIntMy = new("CJ_INT_MY");

    /// <summary>
    /// Saia LTL Freight.
    /// </summary>
    public static readonly ShipmentCarrier SaiaFreight = new("SAIA_FREIGHT");

    /// <summary>
    /// Qxpress.
    /// </summary>
    public static readonly ShipmentCarrier SgQxpress = new("SG_QXPRESS");

    /// <summary>
    /// Nhans Solutions.
    /// </summary>
    public static readonly ShipmentCarrier NhansSolutions = new("NHANS_SOLUTIONS");

    /// <summary>
    /// DPD France.
    /// </summary>
    public static readonly ShipmentCarrier DpdFr = new("DPD_FR");

    /// <summary>
    /// Coordinadora.
    /// </summary>
    public static readonly ShipmentCarrier Coordinadora = new("COORDINADORA");

    /// <summary>
    /// Grupo logistico Andreani.
    /// </summary>
    public static readonly ShipmentCarrier Andreani = new("ANDREANI");

    /// <summary>
    /// Doora Logistics.
    /// </summary>
    public static readonly ShipmentCarrier Doora = new("DOORA");

    /// <summary>
    /// Interparcel New Zealand.
    /// </summary>
    public static readonly ShipmentCarrier InterparcelNz = new("INTERPARCEL_NZ");

    /// <summary>
    /// Jam Express Philippines.
    /// </summary>
    public static readonly ShipmentCarrier PhlJamexpress = new("PHL_JAMEXPRESS");

    /// <summary>
    /// bel_belgium_post.
    /// </summary>
    public static readonly ShipmentCarrier BelBelgiumPost = new("BEL_BELGIUM_POST");

    /// <summary>
    /// us_apc.
    /// </summary>
    public static readonly ShipmentCarrier UsApc = new("US_APC");

    /// <summary>
    /// idn_pos.
    /// </summary>
    public static readonly ShipmentCarrier IdnPos = new("IDN_POS");

    /// <summary>
    /// fr_mondial.
    /// </summary>
    public static readonly ShipmentCarrier FrMondial = new("FR_MONDIAL");

    /// <summary>
    /// DE DHL.
    /// </summary>
    public static readonly ShipmentCarrier DeDhl = new("DE_DHL");

    /// <summary>
    /// hk_rpx.
    /// </summary>
    public static readonly ShipmentCarrier HkRpx = new("HK_RPX");

    /// <summary>
    /// dhl_pieceid.
    /// </summary>
    public static readonly ShipmentCarrier DhlPieceid = new("DHL_PIECEID");

    /// <summary>
    /// vnpost_ems.
    /// </summary>
    public static readonly ShipmentCarrier VnpostEms = new("VNPOST_EMS");

    /// <summary>
    /// rrdonnelley.
    /// </summary>
    public static readonly ShipmentCarrier Rrdonnelley = new("RRDONNELLEY");

    /// <summary>
    /// dpd_de.
    /// </summary>
    public static readonly ShipmentCarrier DpdDe = new("DPD_DE");

    /// <summary>
    /// delcart_in.
    /// </summary>
    public static readonly ShipmentCarrier DelcartIn = new("DELCART_IN");

    /// <summary>
    /// imexglobalsolutions.
    /// </summary>
    public static readonly ShipmentCarrier Imexglobalsolutions = new("IMEXGLOBALSOLUTIONS");

    /// <summary>
    /// ACOMMERCE.
    /// </summary>
    public static readonly ShipmentCarrier Acommerce = new("ACOMMERCE");

    /// <summary>
    /// eurodis.
    /// </summary>
    public static readonly ShipmentCarrier Eurodis = new("EURODIS");

    /// <summary>
    /// CANPAR.
    /// </summary>
    public static readonly ShipmentCarrier Canpar = new("CANPAR");

    /// <summary>
    /// GLS.
    /// </summary>
    public static readonly ShipmentCarrier Gls = new("GLS");

    /// <summary>
    /// Ecom Express.
    /// </summary>
    public static readonly ShipmentCarrier IndEcom = new("IND_ECOM");

    /// <summary>
    /// Envialia.
    /// </summary>
    public static readonly ShipmentCarrier EspEnvialia = new("ESP_ENVIALIA");

    /// <summary>
    /// dhl UK.
    /// </summary>
    public static readonly ShipmentCarrier DhlUk = new("DHL_UK");

    /// <summary>
    /// SMSA Express.
    /// </summary>
    public static readonly ShipmentCarrier SmsaExpress = new("SMSA_EXPRESS");

    /// <summary>
    /// TNT France.
    /// </summary>
    public static readonly ShipmentCarrier TntFr = new("TNT_FR");

    /// <summary>
    /// DEX-I courier.
    /// </summary>
    public static readonly ShipmentCarrier DexI = new("DEX_I");

    /// <summary>
    /// Budbee courier.
    /// </summary>
    public static readonly ShipmentCarrier BudbeeWebhook = new("BUDBEE_WEBHOOK");

    /// <summary>
    /// Copa Airlines Courier.
    /// </summary>
    public static readonly ShipmentCarrier CopaCourier = new("COPA_COURIER");

    /// <summary>
    /// Vietnam Post.
    /// </summary>
    public static readonly ShipmentCarrier VnmVietnamPost = new("VNM_VIETNAM_POST");

    /// <summary>
    /// DPD HongKong.
    /// </summary>
    public static readonly ShipmentCarrier DpdHk = new("DPD_HK");

    /// <summary>
    /// Toll New Zealand.
    /// </summary>
    public static readonly ShipmentCarrier TollNz = new("TOLL_NZ");

    /// <summary>
    /// Echo courier.
    /// </summary>
    public static readonly ShipmentCarrier Echo = new("ECHO");

    /// <summary>
    /// FedEx® Freight.
    /// </summary>
    public static readonly ShipmentCarrier FedexFr = new("FEDEX_FR");

    /// <summary>
    /// Border Express.
    /// </summary>
    public static readonly ShipmentCarrier Borderexpress = new("BORDEREXPRESS");

    /// <summary>
    /// MailPlus (Japan).
    /// </summary>
    public static readonly ShipmentCarrier MailplusJpn = new("MAILPLUS_JPN");

    /// <summary>
    /// TNT UK Reference.
    /// </summary>
    public static readonly ShipmentCarrier TntUkRefr = new("TNT_UK_REFR");

    /// <summary>
    /// KEC courier.
    /// </summary>
    public static readonly ShipmentCarrier Kec = new("KEC");

    /// <summary>
    /// DPD Romania.
    /// </summary>
    public static readonly ShipmentCarrier DpdRo = new("DPD_RO");

    /// <summary>
    /// TNT_JP.
    /// </summary>
    public static readonly ShipmentCarrier TntJp = new("TNT_JP");

    /// <summary>
    /// TH_CJ.
    /// </summary>
    public static readonly ShipmentCarrier ThCj = new("TH_CJ");

    /// <summary>
    /// EC_CN.
    /// </summary>
    public static readonly ShipmentCarrier EcCn = new("EC_CN");

    /// <summary>
    /// FASTWAY_UK.
    /// </summary>
    public static readonly ShipmentCarrier FastwayUk = new("FASTWAY_UK");

    /// <summary>
    /// FASTWAY_US.
    /// </summary>
    public static readonly ShipmentCarrier FastwayUs = new("FASTWAY_US");

    /// <summary>
    /// GLS_DE.
    /// </summary>
    public static readonly ShipmentCarrier GlsDe = new("GLS_DE");

    /// <summary>
    /// GLS_ES.
    /// </summary>
    public static readonly ShipmentCarrier GlsEs = new("GLS_ES");

    /// <summary>
    /// GLS_FR.
    /// </summary>
    public static readonly ShipmentCarrier GlsFr = new("GLS_FR");

    /// <summary>
    /// MONDIAL_BE.
    /// </summary>
    public static readonly ShipmentCarrier MondialBe = new("MONDIAL_BE");

    /// <summary>
    /// SGT_IT.
    /// </summary>
    public static readonly ShipmentCarrier SgtIt = new("SGT_IT");

    /// <summary>
    /// TNT_CN.
    /// </summary>
    public static readonly ShipmentCarrier TntCn = new("TNT_CN");

    /// <summary>
    /// TNT_DE.
    /// </summary>
    public static readonly ShipmentCarrier TntDe = new("TNT_DE");

    /// <summary>
    /// TNT_ES.
    /// </summary>
    public static readonly ShipmentCarrier TntEs = new("TNT_ES");

    /// <summary>
    /// TNT_PL.
    /// </summary>
    public static readonly ShipmentCarrier TntPl = new("TNT_PL");

    /// <summary>
    /// PARCELFORCE.
    /// </summary>
    public static readonly ShipmentCarrier Parcelforce = new("PARCELFORCE");

    /// <summary>
    /// SWISS POST.
    /// </summary>
    public static readonly ShipmentCarrier SwissPost = new("SWISS_POST");

    /// <summary>
    /// TOLL IPEC.
    /// </summary>
    public static readonly ShipmentCarrier TollIpec = new("TOLL_IPEC");

    /// <summary>
    /// AIR 21.
    /// </summary>
    public static readonly ShipmentCarrier Air21 = new("AIR_21");

    /// <summary>
    /// AIRSPEED.
    /// </summary>
    public static readonly ShipmentCarrier Airspeed = new("AIRSPEED");

    /// <summary>
    /// BERT.
    /// </summary>
    public static readonly ShipmentCarrier Bert = new("BERT");

    /// <summary>
    /// BLUEDART.
    /// </summary>
    public static readonly ShipmentCarrier Bluedart = new("BLUEDART");

    /// <summary>
    /// COLLECTPLUS.
    /// </summary>
    public static readonly ShipmentCarrier Collectplus = new("COLLECTPLUS");

    /// <summary>
    /// COURIERPLUS.
    /// </summary>
    public static readonly ShipmentCarrier Courierplus = new("COURIERPLUS");

    /// <summary>
    /// COURIER POST.
    /// </summary>
    public static readonly ShipmentCarrier CourierPost = new("COURIER_POST");

    /// <summary>
    /// dhl_global_mail.
    /// </summary>
    public static readonly ShipmentCarrier DhlGlobalMail = new("DHL_GLOBAL_MAIL");

    /// <summary>
    /// dpd_uk.
    /// </summary>
    public static readonly ShipmentCarrier DpdUk = new("DPD_UK");

    /// <summary>
    /// DELTEC DE.
    /// </summary>
    public static readonly ShipmentCarrier DeltecDe = new("DELTEC_DE");

    /// <summary>
    /// deutsche_de.
    /// </summary>
    public static readonly ShipmentCarrier DeutscheDe = new("DEUTSCHE_DE");

    /// <summary>
    /// DOTZOT.
    /// </summary>
    public static readonly ShipmentCarrier Dotzot = new("DOTZOT");

    /// <summary>
    /// elta_gr.
    /// </summary>
    public static readonly ShipmentCarrier EltaGr = new("ELTA_GR");

    /// <summary>
    /// ems_cn.
    /// </summary>
    public static readonly ShipmentCarrier EmsCn = new("EMS_CN");

    /// <summary>
    /// ECARGO.
    /// </summary>
    public static readonly ShipmentCarrier Ecargo = new("ECARGO");

    /// <summary>
    /// ENSENDA.
    /// </summary>
    public static readonly ShipmentCarrier Ensenda = new("ENSENDA");

    /// <summary>
    /// fercam_it.
    /// </summary>
    public static readonly ShipmentCarrier FercamIt = new("FERCAM_IT");

    /// <summary>
    /// fastway_za.
    /// </summary>
    public static readonly ShipmentCarrier FastwayZa = new("FASTWAY_ZA");

    /// <summary>
    /// fastway_au.
    /// </summary>
    public static readonly ShipmentCarrier FastwayAu = new("FASTWAY_AU");

    /// <summary>
    /// first_logisitcs.
    /// </summary>
    public static readonly ShipmentCarrier FirstLogisitcs = new("FIRST_LOGISITCS");

    /// <summary>
    /// GEODIS.
    /// </summary>
    public static readonly ShipmentCarrier Geodis = new("GEODIS");

    /// <summary>
    /// GLOBEGISTICS.
    /// </summary>
    public static readonly ShipmentCarrier Globegistics = new("GLOBEGISTICS");

    /// <summary>
    /// GREYHOUND.
    /// </summary>
    public static readonly ShipmentCarrier Greyhound = new("GREYHOUND");

    /// <summary>
    /// jetship_my.
    /// </summary>
    public static readonly ShipmentCarrier JetshipMy = new("JETSHIP_MY");

    /// <summary>
    /// LION PARCEL.
    /// </summary>
    public static readonly ShipmentCarrier LionParcel = new("LION_PARCEL");

    /// <summary>
    /// AEROFLASH.
    /// </summary>
    public static readonly ShipmentCarrier Aeroflash = new("AEROFLASH");

    /// <summary>
    /// ONTRAC.
    /// </summary>
    public static readonly ShipmentCarrier Ontrac = new("ONTRAC");

    /// <summary>
    /// SAGAWA.
    /// </summary>
    public static readonly ShipmentCarrier Sagawa = new("SAGAWA");

    /// <summary>
    /// SIODEMKA.
    /// </summary>
    public static readonly ShipmentCarrier Siodemka = new("SIODEMKA");

    /// <summary>
    /// startrack.
    /// </summary>
    public static readonly ShipmentCarrier Startrack = new("STARTRACK");

    /// <summary>
    /// tnt_au.
    /// </summary>
    public static readonly ShipmentCarrier TntAu = new("TNT_AU");

    /// <summary>
    /// tnt_it.
    /// </summary>
    public static readonly ShipmentCarrier TntIt = new("TNT_IT");

    /// <summary>
    /// TRANSMISSION.
    /// </summary>
    public static readonly ShipmentCarrier Transmission = new("TRANSMISSION");

    /// <summary>
    /// YAMATO.
    /// </summary>
    public static readonly ShipmentCarrier Yamato = new("YAMATO");

    /// <summary>
    /// dhl_it.
    /// </summary>
    public static readonly ShipmentCarrier DhlIt = new("DHL_IT");

    /// <summary>
    /// dhl_at.
    /// </summary>
    public static readonly ShipmentCarrier DhlAt = new("DHL_AT");

    /// <summary>
    /// LOGISTICSWORLDWIDE KR.
    /// </summary>
    public static readonly ShipmentCarrier LogisticsworldwideKr = new("LOGISTICSWORLDWIDE_KR");

    /// <summary>
    /// gls_spain.
    /// </summary>
    public static readonly ShipmentCarrier GlsSpain = new("GLS_SPAIN");

    /// <summary>
    /// amazon_uk_api.
    /// </summary>
    public static readonly ShipmentCarrier AmazonUkApi = new("AMAZON_UK_API");

    /// <summary>
    /// dpd_fr_reference.
    /// </summary>
    public static readonly ShipmentCarrier DpdFrReference = new("DPD_FR_REFERENCE");

    /// <summary>
    /// dhlparcel_uk.
    /// </summary>
    public static readonly ShipmentCarrier DhlparcelUk = new("DHLPARCEL_UK");

    /// <summary>
    /// megasave.
    /// </summary>
    public static readonly ShipmentCarrier Megasave = new("MEGASAVE");

    /// <summary>
    /// qualitypost.
    /// </summary>
    public static readonly ShipmentCarrier Qualitypost = new("QUALITYPOST");

    /// <summary>
    /// ids_logistics.
    /// </summary>
    public static readonly ShipmentCarrier IdsLogistics = new("IDS_LOGISTICS");

    /// <summary>
    /// joyingbox.
    /// </summary>
    public static readonly ShipmentCarrier Joyingbox = new("JOYINGBOX");

    /// <summary>
    /// panther_order_number.
    /// </summary>
    public static readonly ShipmentCarrier PantherOrderNumber = new("PANTHER_ORDER_NUMBER");

    /// <summary>
    /// watkins_shepard.
    /// </summary>
    public static readonly ShipmentCarrier WatkinsShepard = new("WATKINS_SHEPARD");

    /// <summary>
    /// fasttrack.
    /// </summary>
    public static readonly ShipmentCarrier Fasttrack = new("FASTTRACK");

    /// <summary>
    /// up_express.
    /// </summary>
    public static readonly ShipmentCarrier UpExpress = new("UP_EXPRESS");

    /// <summary>
    /// elogistica.
    /// </summary>
    public static readonly ShipmentCarrier Elogistica = new("ELOGISTICA");

    /// <summary>
    /// ecourier.
    /// </summary>
    public static readonly ShipmentCarrier Ecourier = new("ECOURIER");

    /// <summary>
    /// cj_philippines.
    /// </summary>
    public static readonly ShipmentCarrier CjPhilippines = new("CJ_PHILIPPINES");

    /// <summary>
    /// speedex.
    /// </summary>
    public static readonly ShipmentCarrier Speedex = new("SPEEDEX");

    /// <summary>
    /// orangeconnex.
    /// </summary>
    public static readonly ShipmentCarrier Orangeconnex = new("ORANGECONNEX");

    /// <summary>
    /// tecor.
    /// </summary>
    public static readonly ShipmentCarrier Tecor = new("TECOR");

    /// <summary>
    /// saee.
    /// </summary>
    public static readonly ShipmentCarrier Saee = new("SAEE");

    /// <summary>
    /// gls_italy_ftp.
    /// </summary>
    public static readonly ShipmentCarrier GlsItalyFtp = new("GLS_ITALY_FTP");

    /// <summary>
    /// delivere.
    /// </summary>
    public static readonly ShipmentCarrier Delivere = new("DELIVERE");

    /// <summary>
    /// yycom.
    /// </summary>
    public static readonly ShipmentCarrier Yycom = new("YYCOM");

    /// <summary>
    /// Adicional Logistics.
    /// </summary>
    public static readonly ShipmentCarrier AdicionalPt = new("ADICIONAL_PT");

    /// <summary>
    /// DKSH.
    /// </summary>
    public static readonly ShipmentCarrier Dksh = new("DKSH");

    /// <summary>
    /// Nippon Express.
    /// </summary>
    public static readonly ShipmentCarrier NipponExpressFtp = new("NIPPON_EXPRESS_FTP");

    /// <summary>
    /// GO Logistics &amp; Storage.
    /// </summary>
    public static readonly ShipmentCarrier Gols = new("GOLS");

    /// <summary>
    /// FUJIE EXPRESS.
    /// </summary>
    public static readonly ShipmentCarrier Fujexp = new("FUJEXP");

    /// <summary>
    /// QTrack.
    /// </summary>
    public static readonly ShipmentCarrier Qtrack = new("QTRACK");

    /// <summary>
    /// OM LOGISTICS LTD.
    /// </summary>
    public static readonly ShipmentCarrier OmlogisticsApi = new("OMLOGISTICS_API");

    /// <summary>
    /// GDPharm Logistics.
    /// </summary>
    public static readonly ShipmentCarrier Gdpharm = new("GDPHARM");

    /// <summary>
    /// MISUMI Group Inc..
    /// </summary>
    public static readonly ShipmentCarrier MisumiCn = new("MISUMI_CN");

    /// <summary>
    /// Rivo.
    /// </summary>
    public static readonly ShipmentCarrier AirCanada = new("AIR_CANADA");

    /// <summary>
    /// City Express.
    /// </summary>
    public static readonly ShipmentCarrier City56Webhook = new("CITY56_WEBHOOK");

    /// <summary>
    /// Sagawa.
    /// </summary>
    public static readonly ShipmentCarrier SagawaApi = new("SAGAWA_API");

    /// <summary>
    /// KedaEX.
    /// </summary>
    public static readonly ShipmentCarrier Kedaex = new("KEDAEX");

    /// <summary>
    /// Pgeon.
    /// </summary>
    public static readonly ShipmentCarrier PgeonApi = new("PGEON_API");

    /// <summary>
    /// We World Express.
    /// </summary>
    public static readonly ShipmentCarrier Weworldexpress = new("WEWORLDEXPRESS");

    /// <summary>
    /// J&amp;T International logistics.
    /// </summary>
    public static readonly ShipmentCarrier JtLogistics = new("JT_LOGISTICS");

    /// <summary>
    /// Trusk France.
    /// </summary>
    public static readonly ShipmentCarrier Trusk = new("TRUSK");

    /// <summary>
    /// ViaXpress.
    /// </summary>
    public static readonly ShipmentCarrier Viaxpress = new("VIAXPRESS");

    /// <summary>
    /// DHL Supply Chain Indonesia.
    /// </summary>
    public static readonly ShipmentCarrier DhlSupplychainId = new("DHL_SUPPLYCHAIN_ID");

    /// <summary>
    /// Zuellig Pharma Korea.
    /// </summary>
    public static readonly ShipmentCarrier ZuelligpharmaSftp = new("ZUELLIGPHARMA_SFTP");

    /// <summary>
    /// Meest.
    /// </summary>
    public static readonly ShipmentCarrier Meest = new("MEEST");

    /// <summary>
    /// Toll Priority.
    /// </summary>
    public static readonly ShipmentCarrier TollPriority = new("TOLL_PRIORITY");

    /// <summary>
    /// Mothership.
    /// </summary>
    public static readonly ShipmentCarrier MothershipApi = new("MOTHERSHIP_API");

    /// <summary>
    /// Capital Transport.
    /// </summary>
    public static readonly ShipmentCarrier Capital = new("CAPITAL");

    /// <summary>
    /// Europacket+.
    /// </summary>
    public static readonly ShipmentCarrier EuropaketApi = new("EUROPAKET_API");

    /// <summary>
    /// HFD.
    /// </summary>
    public static readonly ShipmentCarrier Hfd = new("HFD");

    /// <summary>
    /// Tourline Express.
    /// </summary>
    public static readonly ShipmentCarrier TourlineReference = new("TOURLINE_REFERENCE");

    /// <summary>
    /// GIO Express Inc.
    /// </summary>
    public static readonly ShipmentCarrier GioEcourier = new("GIO_ECOURIER");

    /// <summary>
    /// CN Logistics.
    /// </summary>
    public static readonly ShipmentCarrier CnLogistics = new("CN_LOGISTICS");

    /// <summary>
    /// Pandion.
    /// </summary>
    public static readonly ShipmentCarrier Pandion = new("PANDION");

    /// <summary>
    /// Bpost API.
    /// </summary>
    public static readonly ShipmentCarrier BpostApi = new("BPOST_API");

    /// <summary>
    /// Passport Shipping.
    /// </summary>
    public static readonly ShipmentCarrier Passportshipping = new("PASSPORTSHIPPING");

    /// <summary>
    /// Pakajo World.
    /// </summary>
    public static readonly ShipmentCarrier Pakajo = new("PAKAJO");

    /// <summary>
    /// DACHSER.
    /// </summary>
    public static readonly ShipmentCarrier Dachser = new("DACHSER");

    /// <summary>
    /// Yusen Logistics.
    /// </summary>
    public static readonly ShipmentCarrier YusenSftp = new("YUSEN_SFTP");

    /// <summary>
    /// Shypmax.
    /// </summary>
    public static readonly ShipmentCarrier Shyplite = new("SHYPLITE");

    /// <summary>
    /// Xingyunyi Logistics.
    /// </summary>
    public static readonly ShipmentCarrier Xyy = new("XYY");

    /// <summary>
    /// Metropolitan Warehouse &amp; Delivery.
    /// </summary>
    public static readonly ShipmentCarrier Mwd = new("MWD");

    /// <summary>
    /// Faxe Cargo.
    /// </summary>
    public static readonly ShipmentCarrier Faxecargo = new("FAXECARGO");

    /// <summary>
    /// Groupe Mazet.
    /// </summary>
    public static readonly ShipmentCarrier Mazet = new("MAZET");

    /// <summary>
    /// First Logistics.
    /// </summary>
    public static readonly ShipmentCarrier FirstLogisticsApi = new("FIRST_LOGISTICS_API");

    /// <summary>
    /// SPRINT PACK.
    /// </summary>
    public static readonly ShipmentCarrier SprintPack = new("SPRINT_PACK");

    /// <summary>
    /// Hermes Germany.
    /// </summary>
    public static readonly ShipmentCarrier HermesDeFtp = new("HERMES_DE_FTP");

    /// <summary>
    /// Concise.
    /// </summary>
    public static readonly ShipmentCarrier Concise = new("CONCISE");

    /// <summary>
    /// Kerry Express TaiWan.
    /// </summary>
    public static readonly ShipmentCarrier KerryExpressTwApi = new("KERRY_EXPRESS_TW_API");

    /// <summary>
    /// EWE Global Express.
    /// </summary>
    public static readonly ShipmentCarrier Ewe = new("EWE");

    /// <summary>
    /// Fast Despatch Logistics Limited.
    /// </summary>
    public static readonly ShipmentCarrier Fastdespatch = new("FASTDESPATCH");

    /// <summary>
    /// AB Custom Group.
    /// </summary>
    public static readonly ShipmentCarrier AbcustomSftp = new("ABCUSTOM_SFTP");

    /// <summary>
    /// Chazki.
    /// </summary>
    public static readonly ShipmentCarrier Chazki = new("CHAZKI");

    /// <summary>
    /// Shippie.
    /// </summary>
    public static readonly ShipmentCarrier Shippie = new("SHIPPIE");

    /// <summary>
    /// GEODIS - Distribution &amp; Express.
    /// </summary>
    public static readonly ShipmentCarrier GeodisApi = new("GEODIS_API");

    /// <summary>
    /// Naqel Express.
    /// </summary>
    public static readonly ShipmentCarrier NaqelExpress = new("NAQEL_EXPRESS");

    /// <summary>
    /// Papa.
    /// </summary>
    public static readonly ShipmentCarrier PapaWebhook = new("PAPA_WEBHOOK");

    /// <summary>
    /// Forward Air.
    /// </summary>
    public static readonly ShipmentCarrier Forwardair = new("FORWARDAIR");

    /// <summary>
    /// Dialogo Logistica.
    /// </summary>
    public static readonly ShipmentCarrier DialogoLogisticaApi = new("DIALOGO_LOGISTICA_API");

    /// <summary>
    /// Lalamove.
    /// </summary>
    public static readonly ShipmentCarrier LalamoveApi = new("LALAMOVE_API");

    /// <summary>
    /// Tomydoor.
    /// </summary>
    public static readonly ShipmentCarrier Tomydoor = new("TOMYDOOR");

    /// <summary>
    /// Kronos Express.
    /// </summary>
    public static readonly ShipmentCarrier KronosWebhook = new("KRONOS_WEBHOOK");

    /// <summary>
    /// J&amp;T CARGO.
    /// </summary>
    public static readonly ShipmentCarrier Jtcargo = new("JTCARGO");

    /// <summary>
    /// T-cat.
    /// </summary>
    public static readonly ShipmentCarrier TCat = new("T_CAT");

    /// <summary>
    /// Concise.
    /// </summary>
    public static readonly ShipmentCarrier ConciseWebhook = new("CONCISE_WEBHOOK");

    /// <summary>
    /// Teleport.
    /// </summary>
    public static readonly ShipmentCarrier TeleportWebhook = new("TELEPORT_WEBHOOK");

    /// <summary>
    /// The Custom Companies.
    /// </summary>
    public static readonly ShipmentCarrier CustomcoApi = new("CUSTOMCO_API");

    /// <summary>
    /// Shopee Xpress.
    /// </summary>
    public static readonly ShipmentCarrier SpxTh = new("SPX_TH");

    /// <summary>
    /// Bollore Logistics.
    /// </summary>
    public static readonly ShipmentCarrier BolloreLogistics = new("BOLLORE_LOGISTICS");

    /// <summary>
    /// ClickLink.
    /// </summary>
    public static readonly ShipmentCarrier ClicklinkSftp = new("CLICKLINK_SFTP");

    /// <summary>
    /// M3 Logistics.
    /// </summary>
    public static readonly ShipmentCarrier M3Logistics = new("M3LOGISTICS");

    /// <summary>
    /// Vietnam Post.
    /// </summary>
    public static readonly ShipmentCarrier VnpostApi = new("VNPOST_API");

    /// <summary>
    /// Axlehire.
    /// </summary>
    public static readonly ShipmentCarrier AxlehireFtp = new("AXLEHIRE_FTP");

    /// <summary>
    /// Shadowfax.
    /// </summary>
    public static readonly ShipmentCarrier Shadowfax = new("SHADOWFAX");

    /// <summary>
    /// EVRi.
    /// </summary>
    public static readonly ShipmentCarrier MyhermesUkApi = new("MYHERMES_UK_API");

    /// <summary>
    /// Daiichi Freight System Inc.
    /// </summary>
    public static readonly ShipmentCarrier Daiichi = new("DAIICHI");

    /// <summary>
    /// Mensajeros Urbanos.
    /// </summary>
    public static readonly ShipmentCarrier MensajerosurbanosApi = new("MENSAJEROSURBANOS_API");

    /// <summary>
    /// PolarSpeed Inc.
    /// </summary>
    public static readonly ShipmentCarrier Polarspeed = new("POLARSPEED");

    /// <summary>
    /// iDexpress Indonesia.
    /// </summary>
    public static readonly ShipmentCarrier IdexpressId = new("IDEXPRESS_ID");

    /// <summary>
    /// Payo.
    /// </summary>
    public static readonly ShipmentCarrier Payo = new("PAYO");

    /// <summary>
    /// Whistl.
    /// </summary>
    public static readonly ShipmentCarrier WhistlSftp = new("WHISTL_SFTP");

    /// <summary>
    /// INTEX Paketdienst GmbH.
    /// </summary>
    public static readonly ShipmentCarrier IntexDe = new("INTEX_DE");

    /// <summary>
    /// Trans2u.
    /// </summary>
    public static readonly ShipmentCarrier Trans2U = new("TRANS2U");

    /// <summary>
    /// Product Care Services Limited.
    /// </summary>
    public static readonly ShipmentCarrier ProductcaregroupSftp = new("PRODUCTCAREGROUP_SFTP");

    /// <summary>
    /// Big Smart.
    /// </summary>
    public static readonly ShipmentCarrier Bigsmart = new("BIGSMART");

    /// <summary>
    /// Expeditors API Reference.
    /// </summary>
    public static readonly ShipmentCarrier ExpeditorsApiRef = new("EXPEDITORS_API_REF");

    /// <summary>
    /// AIT.
    /// </summary>
    public static readonly ShipmentCarrier AitworldwideApi = new("AITWORLDWIDE_API");

    /// <summary>
    /// World Courier.
    /// </summary>
    public static readonly ShipmentCarrier Worldcourier = new("WORLDCOURIER");

    /// <summary>
    /// Quiqup.
    /// </summary>
    public static readonly ShipmentCarrier Quiqup = new("QUIQUP");

    /// <summary>
    /// Agediss.
    /// </summary>
    public static readonly ShipmentCarrier AgedissSftp = new("AGEDISS_SFTP");

    /// <summary>
    /// Andreani.
    /// </summary>
    public static readonly ShipmentCarrier AndreaniApi = new("ANDREANI_API");

    /// <summary>
    /// CRL Express.
    /// </summary>
    public static readonly ShipmentCarrier Crlexpress = new("CRLEXPRESS");

    /// <summary>
    /// SMARTCAT.
    /// </summary>
    public static readonly ShipmentCarrier Smartcat = new("SMARTCAT");

    /// <summary>
    /// Crossflight Limited.
    /// </summary>
    public static readonly ShipmentCarrier Crossflight = new("CROSSFLIGHT");

    /// <summary>
    /// Pro Carrier.
    /// </summary>
    public static readonly ShipmentCarrier Procarrier = new("PROCARRIER");

    /// <summary>
    /// DHL (Reference number).
    /// </summary>
    public static readonly ShipmentCarrier DhlReferenceApi = new("DHL_REFERENCE_API");

    /// <summary>
    /// Seino.
    /// </summary>
    public static readonly ShipmentCarrier SeinoApi = new("SEINO_API");

    /// <summary>
    /// WSP Express.
    /// </summary>
    public static readonly ShipmentCarrier Wspexpress = new("WSPEXPRESS");

    /// <summary>
    /// Kronos Express.
    /// </summary>
    public static readonly ShipmentCarrier Kronos = new("KRONOS");

    /// <summary>
    /// Total Express.
    /// </summary>
    public static readonly ShipmentCarrier TotalExpressApi = new("TOTAL_EXPRESS_API");

    /// <summary>
    /// PARCLL.
    /// </summary>
    public static readonly ShipmentCarrier Parcll = new("PARCLL");

    /// <summary>
    /// Xpedigo.
    /// </summary>
    public static readonly ShipmentCarrier Xpedigo = new("XPEDIGO");

    /// <summary>
    /// StarTrack.
    /// </summary>
    public static readonly ShipmentCarrier StarTrackWebhook = new("STAR_TRACK_WEBHOOK");

    /// <summary>
    /// Georgian Post.
    /// </summary>
    public static readonly ShipmentCarrier Gpost = new("GPOST");

    /// <summary>
    /// UCS.
    /// </summary>
    public static readonly ShipmentCarrier Ucs = new("UCS");

    /// <summary>
    /// DMF.
    /// </summary>
    public static readonly ShipmentCarrier Dmfgroup = new("DMFGROUP");

    /// <summary>
    /// Coordinadora.
    /// </summary>
    public static readonly ShipmentCarrier CoordinadoraApi = new("COORDINADORA_API");

    /// <summary>
    /// Marken.
    /// </summary>
    public static readonly ShipmentCarrier Marken = new("MARKEN");

    /// <summary>
    /// NTL logistics.
    /// </summary>
    public static readonly ShipmentCarrier Ntl = new("NTL");

    /// <summary>
    /// Red je Pakketje.
    /// </summary>
    public static readonly ShipmentCarrier Redjepakketje = new("REDJEPAKKETJE");

    /// <summary>
    /// Allied Express (FTP).
    /// </summary>
    public static readonly ShipmentCarrier AlliedExpressFtp = new("ALLIED_EXPRESS_FTP");

    /// <summary>
    /// Mondial Relay Spain(Punto Pack).
    /// </summary>
    public static readonly ShipmentCarrier MondialrelayEs = new("MONDIALRELAY_ES");

    /// <summary>
    /// Naeko Logistics.
    /// </summary>
    public static readonly ShipmentCarrier NaekoFtp = new("NAEKO_FTP");

    /// <summary>
    /// Mhi.
    /// </summary>
    public static readonly ShipmentCarrier Mhi = new("MHI");

    /// <summary>
    /// Shippify, Inc.
    /// </summary>
    public static readonly ShipmentCarrier Shippify = new("SHIPPIFY");

    /// <summary>
    /// Malca Amit.
    /// </summary>
    public static readonly ShipmentCarrier MalcaAmitApi = new("MALCA_AMIT_API");

    /// <summary>
    /// J&amp;T Express Singapore.
    /// </summary>
    public static readonly ShipmentCarrier JtexpressSgApi = new("JTEXPRESS_SG_API");

    /// <summary>
    /// DACHSER.
    /// </summary>
    public static readonly ShipmentCarrier DachserWeb = new("DACHSER_WEB");

    /// <summary>
    /// Flight Logistics Group.
    /// </summary>
    public static readonly ShipmentCarrier Flightlg = new("FLIGHTLG");

    /// <summary>
    /// Cago.
    /// </summary>
    public static readonly ShipmentCarrier Cago = new("CAGO");

    /// <summary>
    /// ComOne Express.
    /// </summary>
    public static readonly ShipmentCarrier Com1Express = new("COM1EXPRESS");

    /// <summary>
    /// Tonami.
    /// </summary>
    public static readonly ShipmentCarrier TonamiFtp = new("TONAMI_FTP");

    /// <summary>
    /// PACKFLEET.
    /// </summary>
    public static readonly ShipmentCarrier Packfleet = new("PACKFLEET");

    /// <summary>
    /// Purolator International.
    /// </summary>
    public static readonly ShipmentCarrier PurolatorInternational = new("PUROLATOR_INTERNATIONAL");

    /// <summary>
    /// Wineshipping.
    /// </summary>
    public static readonly ShipmentCarrier WineshippingWebhook = new("WINESHIPPING_WEBHOOK");

    /// <summary>
    /// DHL Spain Domestic.
    /// </summary>
    public static readonly ShipmentCarrier DhlEsSftp = new("DHL_ES_SFTP");

    /// <summary>
    /// 網家速配股份有限公司.
    /// </summary>
    public static readonly ShipmentCarrier PchomeApi = new("PCHOME_API");

    /// <summary>
    /// Czech Post.
    /// </summary>
    public static readonly ShipmentCarrier CeskapostaApi = new("CESKAPOSTA_API");

    /// <summary>
    /// Go Rush.
    /// </summary>
    public static readonly ShipmentCarrier Gorush = new("GORUSH");

    /// <summary>
    /// HomeRunner.
    /// </summary>
    public static readonly ShipmentCarrier Homerunner = new("HOMERUNNER");

    /// <summary>
    /// Amazon order.
    /// </summary>
    public static readonly ShipmentCarrier AmazonOrder = new("AMAZON_ORDER");

    /// <summary>
    /// Estes Forwarding Worldwide.
    /// </summary>
    public static readonly ShipmentCarrier EfwnowApi = new("EFWNOW_API");

    /// <summary>
    /// CBL Logistica (API).
    /// </summary>
    public static readonly ShipmentCarrier CblLogisticaApi = new("CBL_LOGISTICA_API");

    /// <summary>
    /// NimbusPost.
    /// </summary>
    public static readonly ShipmentCarrier Nimbuspost = new("NIMBUSPOST");

    /// <summary>
    /// Logwin Logistics.
    /// </summary>
    public static readonly ShipmentCarrier LogwinLogistics = new("LOGWIN_LOGISTICS");

    /// <summary>
    /// Sequoialog.
    /// </summary>
    public static readonly ShipmentCarrier NowlogApi = new("NOWLOG_API");

    /// <summary>
    /// DPD Netherlands.
    /// </summary>
    public static readonly ShipmentCarrier DpdNl = new("DPD_NL");

    /// <summary>
    /// Dependable Supply Chain Services.
    /// </summary>
    public static readonly ShipmentCarrier Godependable = new("GODEPENDABLE");

    /// <summary>
    /// Top Ideal Express.
    /// </summary>
    public static readonly ShipmentCarrier Esdex = new("ESDEX");

    /// <summary>
    /// Kiitäjät.
    /// </summary>
    public static readonly ShipmentCarrier LogisystemsSftp = new("LOGISYSTEMS_SFTP");

    /// <summary>
    /// Expeditors.
    /// </summary>
    public static readonly ShipmentCarrier Expeditors = new("EXPEDITORS");

    /// <summary>
    /// Snt Global Etrax.
    /// </summary>
    public static readonly ShipmentCarrier SntglobalApi = new("SNTGLOBAL_API");

    /// <summary>
    /// ShipX.
    /// </summary>
    public static readonly ShipmentCarrier Shipx = new("SHIPX");

    /// <summary>
    /// Quickstat Courier LLC.
    /// </summary>
    public static readonly ShipmentCarrier QintlApi = new("QINTL_API");

    /// <summary>
    /// Packs.
    /// </summary>
    public static readonly ShipmentCarrier Packs = new("PACKS");

    /// <summary>
    /// PostNL International.
    /// </summary>
    public static readonly ShipmentCarrier PostnlInternational = new("POSTNL_INTERNATIONAL");

    /// <summary>
    /// Amazon.
    /// </summary>
    public static readonly ShipmentCarrier AmazonEmailPush = new("AMAZON_EMAIL_PUSH");

    /// <summary>
    /// DHL.
    /// </summary>
    public static readonly ShipmentCarrier DhlApi = new("DHL_API");

    /// <summary>
    /// Shopee Express.
    /// </summary>
    public static readonly ShipmentCarrier Spx = new("SPX");

    /// <summary>
    /// AxleHire.
    /// </summary>
    public static readonly ShipmentCarrier Axlehire = new("AXLEHIRE");

    /// <summary>
    /// ICS COURIER.
    /// </summary>
    public static readonly ShipmentCarrier Icscourier = new("ICSCOURIER");

    /// <summary>
    /// Dialogo Logistica.
    /// </summary>
    public static readonly ShipmentCarrier DialogoLogistica = new("DIALOGO_LOGISTICA");

    /// <summary>
    /// ShunBang Express.
    /// </summary>
    public static readonly ShipmentCarrier ShunbangExpress = new("SHUNBANG_EXPRESS");

    /// <summary>
    /// TCS.
    /// </summary>
    public static readonly ShipmentCarrier TcsApi = new("TCS_API");

    /// <summary>
    /// SF Express China.
    /// </summary>
    public static readonly ShipmentCarrier SfExpressCn = new("SF_EXPRESS_CN");

    /// <summary>
    /// Packeta.
    /// </summary>
    public static readonly ShipmentCarrier Packeta = new("PACKETA");

    /// <summary>
    /// Teliway SIC Express.
    /// </summary>
    public static readonly ShipmentCarrier SicTeliway = new("SIC_TELIWAY");

    /// <summary>
    /// Mondial Relay France.
    /// </summary>
    public static readonly ShipmentCarrier MondialrelayFr = new("MONDIALRELAY_FR");

    /// <summary>
    /// InTime.
    /// </summary>
    public static readonly ShipmentCarrier IntimeFtp = new("INTIME_FTP");

    /// <summary>
    /// 京东物流.
    /// </summary>
    public static readonly ShipmentCarrier JdExpress = new("JD_EXPRESS");

    /// <summary>
    /// Fastbox.
    /// </summary>
    public static readonly ShipmentCarrier Fastbox = new("FASTBOX");

    /// <summary>
    /// Patheon Logistics.
    /// </summary>
    public static readonly ShipmentCarrier Patheon = new("PATHEON");

    /// <summary>
    /// India Post Domestic.
    /// </summary>
    public static readonly ShipmentCarrier IndiaPost = new("INDIA_POST");

    /// <summary>
    /// Tipsa Reference.
    /// </summary>
    public static readonly ShipmentCarrier TipsaRef = new("TIPSA_REF");

    /// <summary>
    /// Eco Freight.
    /// </summary>
    public static readonly ShipmentCarrier Ecofreight = new("ECOFREIGHT");

    /// <summary>
    /// VOX SOLUCION EMPRESARIAL SRL.
    /// </summary>
    public static readonly ShipmentCarrier Vox = new("VOX");

    /// <summary>
    /// Direct Freight Express.
    /// </summary>
    public static readonly ShipmentCarrier DirectfreightAuRef = new("DIRECTFREIGHT_AU_REF");

    /// <summary>
    /// Best Transport.
    /// </summary>
    public static readonly ShipmentCarrier BesttransportSftp = new("BESTTRANSPORT_SFTP");

    /// <summary>
    /// Australia Post.
    /// </summary>
    public static readonly ShipmentCarrier AustraliaPostApi = new("AUSTRALIA_POST_API");

    /// <summary>
    /// FragilePAK.
    /// </summary>
    public static readonly ShipmentCarrier FragilepakSftp = new("FRAGILEPAK_SFTP");

    /// <summary>
    /// FlipXpress.
    /// </summary>
    public static readonly ShipmentCarrier Flipxp = new("FLIPXP");

    /// <summary>
    /// Value Logistics.
    /// </summary>
    public static readonly ShipmentCarrier ValueWebhook = new("VALUE_WEBHOOK");

    /// <summary>
    /// Daeshin.
    /// </summary>
    public static readonly ShipmentCarrier Daeshin = new("DAESHIN");

    /// <summary>
    /// Sherpa.
    /// </summary>
    public static readonly ShipmentCarrier Sherpa = new("SHERPA");

    /// <summary>
    /// Metropolitan Warehouse &amp; Delivery.
    /// </summary>
    public static readonly ShipmentCarrier MwdApi = new("MWD_API");

    /// <summary>
    /// SmartKargo.
    /// </summary>
    public static readonly ShipmentCarrier Smartkargo = new("SMARTKARGO");

    /// <summary>
    /// DNJ Express.
    /// </summary>
    public static readonly ShipmentCarrier DnjExpress = new("DNJ_EXPRESS");

    /// <summary>
    /// Go People.
    /// </summary>
    public static readonly ShipmentCarrier Gopeople = new("GOPEOPLE");

    /// <summary>
    /// mySendle.
    /// </summary>
    public static readonly ShipmentCarrier MysendleApi = new("MYSENDLE_API");

    /// <summary>
    /// Aramex.
    /// </summary>
    public static readonly ShipmentCarrier AramexApi = new("ARAMEX_API");

    /// <summary>
    /// Pidge.
    /// </summary>
    public static readonly ShipmentCarrier Pidge = new("PIDGE");

    /// <summary>
    /// TP Logistic.
    /// </summary>
    public static readonly ShipmentCarrier Thaiparcels = new("THAIPARCELS");

    /// <summary>
    /// Panther Reference.
    /// </summary>
    public static readonly ShipmentCarrier PantherReferenceApi = new("PANTHER_REFERENCE_API");

    /// <summary>
    /// Posta Plus.
    /// </summary>
    public static readonly ShipmentCarrier Postaplus = new("POSTAPLUS");

    /// <summary>
    /// BUFFALO.
    /// </summary>
    public static readonly ShipmentCarrier Buffalo = new("BUFFALO");

    /// <summary>
    /// U-ENVIOS.
    /// </summary>
    public static readonly ShipmentCarrier UEnvios = new("U_ENVIOS");

    /// <summary>
    /// Elite Express.
    /// </summary>
    public static readonly ShipmentCarrier EliteCo = new("ELITE_CO");

    /// <summary>
    /// Roche Internal Courier.
    /// </summary>
    public static readonly ShipmentCarrier RocheInternalSftp = new("ROCHE_INTERNAL_SFTP");

    /// <summary>
    /// DB Schenker Iceland.
    /// </summary>
    public static readonly ShipmentCarrier DbschenkerIceland = new("DBSCHENKER_ICELAND");

    /// <summary>
    /// TNT France Reference.
    /// </summary>
    public static readonly ShipmentCarrier TntFrReference = new("TNT_FR_REFERENCE");

    /// <summary>
    /// Newgistics API.
    /// </summary>
    public static readonly ShipmentCarrier Newgisticsapi = new("NEWGISTICSAPI");

    /// <summary>
    /// Glovo.
    /// </summary>
    public static readonly ShipmentCarrier Glovo = new("GLOVO");

    /// <summary>
    /// G.I.G.
    /// </summary>
    public static readonly ShipmentCarrier GwlogisApi = new("GWLOGIS_API");

    /// <summary>
    /// Spreetail.
    /// </summary>
    public static readonly ShipmentCarrier SpreetailApi = new("SPREETAIL_API");

    /// <summary>
    /// Moova.
    /// </summary>
    public static readonly ShipmentCarrier Moova = new("MOOVA");

    /// <summary>
    /// Plycon Transportation Group.
    /// </summary>
    public static readonly ShipmentCarrier Plycongroup = new("PLYCONGROUP");

    /// <summary>
    /// USPS Informed Visibility - Webhook.
    /// </summary>
    public static readonly ShipmentCarrier UspsWebhook = new("USPS_WEBHOOK");

    /// <summary>
    /// maergo.
    /// </summary>
    public static readonly ShipmentCarrier Reimaginedelivery = new("REIMAGINEDELIVERY");

    /// <summary>
    /// Eurodifarm.
    /// </summary>
    public static readonly ShipmentCarrier EdfFtp = new("EDF_FTP");

    /// <summary>
    /// DAO365.
    /// </summary>
    public static readonly ShipmentCarrier Dao365 = new("DAO365");

    /// <summary>
    /// BioCair.
    /// </summary>
    public static readonly ShipmentCarrier BiocairFtp = new("BIOCAIR_FTP");

    /// <summary>
    /// Ransa.
    /// </summary>
    public static readonly ShipmentCarrier RansaWebhook = new("RANSA_WEBHOOK");

    /// <summary>
    /// SHIPXPRESS.
    /// </summary>
    public static readonly ShipmentCarrier Shipxpres = new("SHIPXPRES");

    /// <summary>
    /// Courant Plus.
    /// </summary>
    public static readonly ShipmentCarrier CourantPlusApi = new("COURANT_PLUS_API");

    /// <summary>
    /// SHIPA.
    /// </summary>
    public static readonly ShipmentCarrier Shipa = new("SHIPA");

    /// <summary>
    /// Home Logistics.
    /// </summary>
    public static readonly ShipmentCarrier Homelogistics = new("HOMELOGISTICS");

    /// <summary>
    /// DX.
    /// </summary>
    public static readonly ShipmentCarrier Dx = new("DX");

    /// <summary>
    /// Poste Italiane Paccocelere.
    /// </summary>
    public static readonly ShipmentCarrier PosteItalianePaccocelere = new("POSTE_ITALIANE_PACCOCELERE");

    /// <summary>
    /// Toll Group.
    /// </summary>
    public static readonly ShipmentCarrier TollWebhook = new("TOLL_WEBHOOK");

    /// <summary>
    /// LCT do Brasil.
    /// </summary>
    public static readonly ShipmentCarrier LctbrApi = new("LCTBR_API");

    /// <summary>
    /// DX Freight.
    /// </summary>
    public static readonly ShipmentCarrier DxFreight = new("DX_FREIGHT");

    /// <summary>
    /// DHL Express.
    /// </summary>
    public static readonly ShipmentCarrier DhlSftp = new("DHL_SFTP");

    /// <summary>
    /// Shiprocket X.
    /// </summary>
    public static readonly ShipmentCarrier Shiprocket = new("SHIPROCKET");

    /// <summary>
    /// Uber.
    /// </summary>
    public static readonly ShipmentCarrier UberWebhook = new("UBER_WEBHOOK");

    /// <summary>
    /// Stat Overnight.
    /// </summary>
    public static readonly ShipmentCarrier Statovernight = new("STATOVERNIGHT");

    /// <summary>
    /// Burd Delivery.
    /// </summary>
    public static readonly ShipmentCarrier Burd = new("BURD");

    /// <summary>
    /// Fastship Express.
    /// </summary>
    public static readonly ShipmentCarrier Fastship = new("FASTSHIP");

    /// <summary>
    /// IB Venture.
    /// </summary>
    public static readonly ShipmentCarrier IbventureWebhook = new("IBVENTURE_WEBHOOK");

    /// <summary>
    /// Gati-KWE.
    /// </summary>
    public static readonly ShipmentCarrier GatiKweApi = new("GATI_KWE_API");

    /// <summary>
    /// CryoPDP.
    /// </summary>
    public static readonly ShipmentCarrier CryopdpFtp = new("CRYOPDP_FTP");

    /// <summary>
    /// HUBBED.
    /// </summary>
    public static readonly ShipmentCarrier Hubbed = new("HUBBED");

    /// <summary>
    /// Tipsa API.
    /// </summary>
    public static readonly ShipmentCarrier TipsaApi = new("TIPSA_API");

    /// <summary>
    /// Aras Cargo.
    /// </summary>
    public static readonly ShipmentCarrier Araskargo = new("ARASKARGO");

    /// <summary>
    /// Thijs Logistiek.
    /// </summary>
    public static readonly ShipmentCarrier ThijsNl = new("THIJS_NL");

    /// <summary>
    /// ATS Healthcare.
    /// </summary>
    public static readonly ShipmentCarrier AtshealthcareReference = new("ATSHEALTHCARE_REFERENCE");

    /// <summary>
    /// 99minutos.
    /// </summary>
    public static readonly ShipmentCarrier _99Minutos = new("99MINUTOS");

    /// <summary>
    /// Hellenic (Greece) Post.
    /// </summary>
    public static readonly ShipmentCarrier HellenicPost = new("HELLENIC_POST");

    /// <summary>
    /// HSM Global.
    /// </summary>
    public static readonly ShipmentCarrier HsmGlobal = new("HSM_GLOBAL");

    /// <summary>
    /// MNX.
    /// </summary>
    public static readonly ShipmentCarrier Mnx = new("MNX");

    /// <summary>
    /// N&amp;M Transfer Co., Inc..
    /// </summary>
    public static readonly ShipmentCarrier Nmtransfer = new("NMTRANSFER");

    /// <summary>
    /// Logysto.
    /// </summary>
    public static readonly ShipmentCarrier Logysto = new("LOGYSTO");

    /// <summary>
    /// India Post International.
    /// </summary>
    public static readonly ShipmentCarrier IndiaPostInt = new("INDIA_POST_INT");

    /// <summary>
    /// Swiship IN.
    /// </summary>
    public static readonly ShipmentCarrier AmazonFbaSwishipIn = new("AMAZON_FBA_SWISHIP_IN");

    /// <summary>
    /// SRT Transport.
    /// </summary>
    public static readonly ShipmentCarrier SrtTransport = new("SRT_TRANSPORT");

    /// <summary>
    /// Bomi Group.
    /// </summary>
    public static readonly ShipmentCarrier Bomi = new("BOMI");

    /// <summary>
    /// Deliverr.
    /// </summary>
    public static readonly ShipmentCarrier DeliverrSftp = new("DELIVERR_SFTP");

    /// <summary>
    /// HSDEXPRESS.
    /// </summary>
    public static readonly ShipmentCarrier Hsdexpress = new("HSDEXPRESS");

    /// <summary>
    /// SimpleTire.
    /// </summary>
    public static readonly ShipmentCarrier SimpletireWebhook = new("SIMPLETIRE_WEBHOOK");

    /// <summary>
    /// Hunter Express.
    /// </summary>
    public static readonly ShipmentCarrier HunterExpressSftp = new("HUNTER_EXPRESS_SFTP");

    /// <summary>
    /// UPS.
    /// </summary>
    public static readonly ShipmentCarrier UpsApi = new("UPS_API");

    /// <summary>
    /// WOO YOUNG LOGISTICS CO.,LTD..
    /// </summary>
    public static readonly ShipmentCarrier WooyoungLogisticsSftp = new("WOOYOUNG_LOGISTICS_SFTP");

    /// <summary>
    /// PHSE.
    /// </summary>
    public static readonly ShipmentCarrier PhseApi = new("PHSE_API");

    /// <summary>
    /// Wish.
    /// </summary>
    public static readonly ShipmentCarrier WishEmailPush = new("WISH_EMAIL_PUSH");

    /// <summary>
    /// Northline.
    /// </summary>
    public static readonly ShipmentCarrier Northline = new("NORTHLINE");

    /// <summary>
    /// Med Africa Logistics.
    /// </summary>
    public static readonly ShipmentCarrier Medafrica = new("MEDAFRICA");

    /// <summary>
    /// DPD Austria.
    /// </summary>
    public static readonly ShipmentCarrier DpdAtSftp = new("DPD_AT_SFTP");

    /// <summary>
    /// Anteraja.
    /// </summary>
    public static readonly ShipmentCarrier Anteraja = new("ANTERAJA");

    /// <summary>
    /// DHL Global Forwarding API.
    /// </summary>
    public static readonly ShipmentCarrier DhlGlobalForwardingApi = new("DHL_GLOBAL_FORWARDING_API");

    /// <summary>
    /// LBC EXPRESS INC..
    /// </summary>
    public static readonly ShipmentCarrier LbcexpressApi = new("LBCEXPRESS_API");

    /// <summary>
    /// Sims Global.
    /// </summary>
    public static readonly ShipmentCarrier Simsglobal = new("SIMSGLOBAL");

    /// <summary>
    /// CDL Last Mile.
    /// </summary>
    public static readonly ShipmentCarrier Cdldelivers = new("CDLDELIVERS");

    /// <summary>
    /// TYP.
    /// </summary>
    public static readonly ShipmentCarrier Typ = new("TYP");

    /// <summary>
    /// Testing Courier.
    /// </summary>
    public static readonly ShipmentCarrier TestingCourierWebhook = new("TESTING_COURIER_WEBHOOK");

    /// <summary>
    /// Pandago.
    /// </summary>
    public static readonly ShipmentCarrier PandagoApi = new("PANDAGO_API");

    /// <summary>
    /// Royal Mail.
    /// </summary>
    public static readonly ShipmentCarrier RoyalMailFtp = new("ROYAL_MAIL_FTP");

    /// <summary>
    /// Thunder Express Australia.
    /// </summary>
    public static readonly ShipmentCarrier Thunderexpress = new("THUNDEREXPRESS");

    /// <summary>
    /// Secretlab.
    /// </summary>
    public static readonly ShipmentCarrier SecretlabWebhook = new("SECRETLAB_WEBHOOK");

    /// <summary>
    /// Setel Express.
    /// </summary>
    public static readonly ShipmentCarrier Setel = new("SETEL");

    /// <summary>
    /// JD Worldwide.
    /// </summary>
    public static readonly ShipmentCarrier JdWorldwide = new("JD_WORLDWIDE");

    /// <summary>
    /// DPD Russia.
    /// </summary>
    public static readonly ShipmentCarrier DpdRuApi = new("DPD_RU_API");

    /// <summary>
    /// Argents Express Group.
    /// </summary>
    public static readonly ShipmentCarrier ArgentsWebhook = new("ARGENTS_WEBHOOK");

    /// <summary>
    /// Post ONE.
    /// </summary>
    public static readonly ShipmentCarrier Postone = new("POSTONE");

    /// <summary>
    /// Tusk Logistics.
    /// </summary>
    public static readonly ShipmentCarrier Tusklogistics = new("TUSKLOGISTICS");

    /// <summary>
    /// Rhenus Logistics UK.
    /// </summary>
    public static readonly ShipmentCarrier RhenusUkApi = new("RHENUS_UK_API");

    /// <summary>
    /// Yamato Singapore.
    /// </summary>
    public static readonly ShipmentCarrier TaqbinSgApi = new("TAQBIN_SG_API");

    /// <summary>
    /// Inntralog GmbH.
    /// </summary>
    public static readonly ShipmentCarrier InntralogSftp = new("INNTRALOG_SFTP");

    /// <summary>
    /// Day &amp; Ross.
    /// </summary>
    public static readonly ShipmentCarrier Dayross = new("DAYROSS");

    /// <summary>
    /// Correos Express (API).
    /// </summary>
    public static readonly ShipmentCarrier CorreosexpressApi = new("CORREOSEXPRESS_API");

    /// <summary>
    /// International Seur API.
    /// </summary>
    public static readonly ShipmentCarrier InternationalSeurApi = new("INTERNATIONAL_SEUR_API");

    /// <summary>
    /// Yodel API.
    /// </summary>
    public static readonly ShipmentCarrier YodelApi = new("YODEL_API");

    /// <summary>
    /// Hero Express.
    /// </summary>
    public static readonly ShipmentCarrier Heroexpress = new("HEROEXPRESS");

    /// <summary>
    /// DHL supply chain India.
    /// </summary>
    public static readonly ShipmentCarrier DhlSupplychainIn = new("DHL_SUPPLYCHAIN_IN");

    /// <summary>
    /// Urgent Cargus.
    /// </summary>
    public static readonly ShipmentCarrier UrgentCargus = new("URGENT_CARGUS");

    /// <summary>
    /// FRONTdoor Collective.
    /// </summary>
    public static readonly ShipmentCarrier Frontdoorcorp = new("FRONTDOORCORP");

    /// <summary>
    /// J&amp;T Express Philippines.
    /// </summary>
    public static readonly ShipmentCarrier JtexpressPh = new("JTEXPRESS_PH");

    /// <summary>
    /// Parcelstars.
    /// </summary>
    public static readonly ShipmentCarrier ParcelstarsWebhook = new("PARCELSTARS_WEBHOOK");

    /// <summary>
    /// DPD Slovakia.
    /// </summary>
    public static readonly ShipmentCarrier DpdSkSftp = new("DPD_SK_SFTP");

    /// <summary>
    /// Movianto.
    /// </summary>
    public static readonly ShipmentCarrier Movianto = new("MOVIANTO");

    /// <summary>
    /// Ozeparts Shipping.
    /// </summary>
    public static readonly ShipmentCarrier OzepartsShipping = new("OZEPARTS_SHIPPING");

    /// <summary>
    /// KargomKolay (CargoMini).
    /// </summary>
    public static readonly ShipmentCarrier Kargomkolay = new("KARGOMKOLAY");

    /// <summary>
    /// Trunkrs.
    /// </summary>
    public static readonly ShipmentCarrier Trunkrs = new("TRUNKRS");

    /// <summary>
    /// Omni Returns.
    /// </summary>
    public static readonly ShipmentCarrier OmnirpsWebhook = new("OMNIRPS_WEBHOOK");

    /// <summary>
    /// Chile Express.
    /// </summary>
    public static readonly ShipmentCarrier Chilexpress = new("CHILEXPRESS");

    /// <summary>
    /// Testing Courier.
    /// </summary>
    public static readonly ShipmentCarrier TestingCourier = new("TESTING_COURIER");

    /// <summary>
    /// JNE (API).
    /// </summary>
    public static readonly ShipmentCarrier JneApi = new("JNE_API");

    /// <summary>
    /// BJS Distribution, Storage &amp; Couriers - FTP.
    /// </summary>
    public static readonly ShipmentCarrier BjshomedeliveryFtp = new("BJSHOMEDELIVERY_FTP");

    /// <summary>
    /// D Express.
    /// </summary>
    public static readonly ShipmentCarrier DexpressWebhook = new("DEXPRESS_WEBHOOK");

    /// <summary>
    /// USPS API.
    /// </summary>
    public static readonly ShipmentCarrier UspsApi = new("USPS_API");

    /// <summary>
    /// TransVirtual.
    /// </summary>
    public static readonly ShipmentCarrier Transvirtual = new("TRANSVIRTUAL");

    /// <summary>
    /// solistica.
    /// </summary>
    public static readonly ShipmentCarrier SolisticaApi = new("SOLISTICA_API");

    /// <summary>
    /// Chienventure.
    /// </summary>
    public static readonly ShipmentCarrier ChienventureWebhook = new("CHIENVENTURE_WEBHOOK");

    /// <summary>
    /// DPD UK.
    /// </summary>
    public static readonly ShipmentCarrier DpdUkSftp = new("DPD_UK_SFTP");

    /// <summary>
    /// InPost.
    /// </summary>
    public static readonly ShipmentCarrier InpostUk = new("INPOST_UK");

    /// <summary>
    /// Javit.
    /// </summary>
    public static readonly ShipmentCarrier Javit = new("JAVIT");

    /// <summary>
    /// ZTO Express China.
    /// </summary>
    public static readonly ShipmentCarrier ZtoDomestic = new("ZTO_DOMESTIC");

    /// <summary>
    /// DHL Global Forwarding Guatemala.
    /// </summary>
    public static readonly ShipmentCarrier DhlGtApi = new("DHL_GT_API");

    /// <summary>
    /// CEVA Package.
    /// </summary>
    public static readonly ShipmentCarrier CevaTracking = new("CEVA_TRACKING");

    /// <summary>
    /// Komon Express.
    /// </summary>
    public static readonly ShipmentCarrier KomonExpress = new("KOMON_EXPRESS");

    /// <summary>
    /// East West Courier Pte Ltd.
    /// </summary>
    public static readonly ShipmentCarrier EastwestcourierFtp = new("EASTWESTCOURIER_FTP");

    /// <summary>
    /// Danniao.
    /// </summary>
    public static readonly ShipmentCarrier Danniao = new("DANNIAO");

    /// <summary>
    /// Spectran.
    /// </summary>
    public static readonly ShipmentCarrier Spectran = new("SPECTRAN");

    /// <summary>
    /// Deliver-iT.
    /// </summary>
    public static readonly ShipmentCarrier DeliverIt = new("DELIVER_IT");

    /// <summary>
    /// Relais Colis.
    /// </summary>
    public static readonly ShipmentCarrier Relaiscolis = new("RELAISCOLIS");

    /// <summary>
    /// GLS Spain.
    /// </summary>
    public static readonly ShipmentCarrier GlsSpainApi = new("GLS_SPAIN_API");

    /// <summary>
    /// PostPlus.
    /// </summary>
    public static readonly ShipmentCarrier Postplus = new("POSTPLUS");

    /// <summary>
    /// Airterra.
    /// </summary>
    public static readonly ShipmentCarrier Airterra = new("AIRTERRA");

    /// <summary>
    /// GIO Express Ecourier.
    /// </summary>
    public static readonly ShipmentCarrier GioEcourierApi = new("GIO_ECOURIER_API");

    /// <summary>
    /// DPD Switzerland.
    /// </summary>
    public static readonly ShipmentCarrier DpdChSftp = new("DPD_CH_SFTP");

    /// <summary>
    /// FedEx®.
    /// </summary>
    public static readonly ShipmentCarrier FedexApi = new("FEDEX_API");

    /// <summary>
    /// INTERSMARTTRANS &amp; SOLUTIONS SL.
    /// </summary>
    public static readonly ShipmentCarrier Intersmarttrans = new("INTERSMARTTRANS");

    /// <summary>
    /// Hermes UK.
    /// </summary>
    public static readonly ShipmentCarrier HermesUkSftp = new("HERMES_UK_SFTP");

    /// <summary>
    /// Exelot Ltd..
    /// </summary>
    public static readonly ShipmentCarrier ExelotFtp = new("EXELOT_FTP");

    /// <summary>
    /// DHL GLOBAL FORWARDING PANAMÁ.
    /// </summary>
    public static readonly ShipmentCarrier DhlPaApi = new("DHL_PA_API");

    /// <summary>
    /// Vir Transport.
    /// </summary>
    public static readonly ShipmentCarrier VirtransportSftp = new("VIRTRANSPORT_SFTP");

    /// <summary>
    /// Worldnet Logistics.
    /// </summary>
    public static readonly ShipmentCarrier Worldnet = new("WORLDNET");

    /// <summary>
    /// Instabox.
    /// </summary>
    public static readonly ShipmentCarrier InstaboxWebhook = new("INSTABOX_WEBHOOK");

    /// <summary>
    /// Keuhne + Nagel Global.
    /// </summary>
    public static readonly ShipmentCarrier Kng = new("KNG");

    /// <summary>
    /// Flash Express.
    /// </summary>
    public static readonly ShipmentCarrier FlashexpressWebhook = new("FLASHEXPRESS_WEBHOOK");

    /// <summary>
    /// Magyar Posta.
    /// </summary>
    public static readonly ShipmentCarrier MagyarPostaApi = new("MAGYAR_POSTA_API");

    /// <summary>
    /// WeShip.
    /// </summary>
    public static readonly ShipmentCarrier WeshipApi = new("WESHIP_API");

    /// <summary>
    /// Ohi.
    /// </summary>
    public static readonly ShipmentCarrier OhiWebhook = new("OHI_WEBHOOK");

    /// <summary>
    /// MUDITA.
    /// </summary>
    public static readonly ShipmentCarrier Mudita = new("MUDITA");

    /// <summary>
    /// Bluedart.
    /// </summary>
    public static readonly ShipmentCarrier BluedartApi = new("BLUEDART_API");

    /// <summary>
    /// T-cat.
    /// </summary>
    public static readonly ShipmentCarrier TCatApi = new("T_CAT_API");

    /// <summary>
    /// ADS Express.
    /// </summary>
    public static readonly ShipmentCarrier Ads = new("ADS");

    /// <summary>
    /// HR Parcel.
    /// </summary>
    public static readonly ShipmentCarrier HermesIt = new("HERMES_IT");

    /// <summary>
    /// FitzMark.
    /// </summary>
    public static readonly ShipmentCarrier FitzmarkApi = new("FITZMARK_API");

    /// <summary>
    /// Posti API.
    /// </summary>
    public static readonly ShipmentCarrier PostiApi = new("POSTI_API");

    /// <summary>
    /// SMSA Express.
    /// </summary>
    public static readonly ShipmentCarrier SmsaExpressWebhook = new("SMSA_EXPRESS_WEBHOOK");

    /// <summary>
    /// Tamer Logistics.
    /// </summary>
    public static readonly ShipmentCarrier TamergroupWebhook = new("TAMERGROUP_WEBHOOK");

    /// <summary>
    /// Livrapide.
    /// </summary>
    public static readonly ShipmentCarrier Livrapide = new("LIVRAPIDE");

    /// <summary>
    /// Nippon Express.
    /// </summary>
    public static readonly ShipmentCarrier NipponExpress = new("NIPPON_EXPRESS");

    /// <summary>
    /// Better Trucks.
    /// </summary>
    public static readonly ShipmentCarrier Bettertrucks = new("BETTERTRUCKS");

    /// <summary>
    /// FAN COURIER EXPRESS.
    /// </summary>
    public static readonly ShipmentCarrier Fan = new("FAN");

    /// <summary>
    /// USPS Flats (Pitney Bowes).
    /// </summary>
    public static readonly ShipmentCarrier PbUspsflatsFtp = new("PB_USPSFLATS_FTP");

    /// <summary>
    /// Parcel Right.
    /// </summary>
    public static readonly ShipmentCarrier Parcelright = new("PARCELRIGHT");

    /// <summary>
    /// iThink Logistics.
    /// </summary>
    public static readonly ShipmentCarrier Ithinklogistics = new("ITHINKLOGISTICS");

    /// <summary>
    /// Kerry Logistics.
    /// </summary>
    public static readonly ShipmentCarrier KerryExpressThWebhook = new("KERRY_EXPRESS_TH_WEBHOOK");

    /// <summary>
    /// eCoutier.
    /// </summary>
    public static readonly ShipmentCarrier Ecoutier = new("ECOUTIER");

    /// <summary>
    /// SENHONG INTERNATIONAL LOGISTICS.
    /// </summary>
    public static readonly ShipmentCarrier Showl = new("SHOWL");

    /// <summary>
    /// BRT Bartolini API.
    /// </summary>
    public static readonly ShipmentCarrier BrtItApi = new("BRT_IT_API");

    /// <summary>
    /// Rixon Logistics.
    /// </summary>
    public static readonly ShipmentCarrier RixonhkApi = new("RIXONHK_API");

    /// <summary>
    /// DB Schenker.
    /// </summary>
    public static readonly ShipmentCarrier DbschenkerApi = new("DBSCHENKER_API");

    /// <summary>
    /// Ilyang logistics.
    /// </summary>
    public static readonly ShipmentCarrier Ilyanglogis = new("ILYANGLOGIS");

    /// <summary>
    /// Mail Boxes Etc..
    /// </summary>
    public static readonly ShipmentCarrier MailBoxEtc = new("MAIL_BOX_ETC");

    /// <summary>
    /// WeShip.
    /// </summary>
    public static readonly ShipmentCarrier Weship = new("WESHIP");

    /// <summary>
    /// DHL eCommerce Solutions.
    /// </summary>
    public static readonly ShipmentCarrier DhlGlobalMailApi = new("DHL_GLOBAL_MAIL_API");

    /// <summary>
    /// Activos24.
    /// </summary>
    public static readonly ShipmentCarrier Activos24Api = new("ACTIVOS24_API");

    /// <summary>
    /// ATS Healthcare.
    /// </summary>
    public static readonly ShipmentCarrier Atshealthcare = new("ATSHEALTHCARE");

    /// <summary>
    /// Luwjistik.
    /// </summary>
    public static readonly ShipmentCarrier Luwjistik = new("LUWJISTIK");

    /// <summary>
    /// Gebrüder Weiss.
    /// </summary>
    public static readonly ShipmentCarrier GwWorld = new("GW_WORLD");

    /// <summary>
    /// fairsenden.
    /// </summary>
    public static readonly ShipmentCarrier FairsendenApi = new("FAIRSENDEN_API");

    /// <summary>
    /// SerVIP.
    /// </summary>
    public static readonly ShipmentCarrier ServipWebhook = new("SERVIP_WEBHOOK");

    /// <summary>
    /// Swiship.
    /// </summary>
    public static readonly ShipmentCarrier Swiship = new("SWISHIP");

    /// <summary>
    /// Transport Ambientales.
    /// </summary>
    public static readonly ShipmentCarrier Tanet = new("TANET");

    /// <summary>
    /// SHENZHEN HOTSIN CARGO INT'L FORWARDING CO.,LTD.
    /// </summary>
    public static readonly ShipmentCarrier HotsinCargo = new("HOTSIN_CARGO");

    /// <summary>
    /// Direx.
    /// </summary>
    public static readonly ShipmentCarrier Direx = new("DIREX");

    /// <summary>
    /// HuanTong.
    /// </summary>
    public static readonly ShipmentCarrier Huantong = new("HUANTONG");

    /// <summary>
    /// iMile.
    /// </summary>
    public static readonly ShipmentCarrier ImileApi = new("IMILE_API");

    /// <summary>
    /// Au Express.
    /// </summary>
    public static readonly ShipmentCarrier Auexpress = new("AUEXPRESS");

    /// <summary>
    /// NYT SUPPLY CHAIN LOGISTICS Co.,LTD.
    /// </summary>
    public static readonly ShipmentCarrier Nytlogistics = new("NYTLOGISTICS");

    /// <summary>
    /// DSV Futurewave.
    /// </summary>
    public static readonly ShipmentCarrier DsvReference = new("DSV_REFERENCE");

    /// <summary>
    /// Novofarma.
    /// </summary>
    public static readonly ShipmentCarrier NovofarmaWebhook = new("NOVOFARMA_WEBHOOK");

    /// <summary>
    /// AIT.
    /// </summary>
    public static readonly ShipmentCarrier AitworldwideSftp = new("AITWORLDWIDE_SFTP");

    /// <summary>
    /// Olive.
    /// </summary>
    public static readonly ShipmentCarrier Shopolive = new("SHOPOLIVE");

    /// <summary>
    /// Fast &amp; Furious.
    /// </summary>
    public static readonly ShipmentCarrier FnfZa = new("FNF_ZA");

    /// <summary>
    /// DHL eCommerce Greater China.
    /// </summary>
    public static readonly ShipmentCarrier DhlEcommerceGc = new("DHL_ECOMMERCE_GC");

    /// <summary>
    /// Fetchr.
    /// </summary>
    public static readonly ShipmentCarrier Fetchr = new("FETCHR");

    /// <summary>
    /// Starlinks Global.
    /// </summary>
    public static readonly ShipmentCarrier StarlinksApi = new("STARLINKS_API");

    /// <summary>
    /// YYEXPRESS.
    /// </summary>
    public static readonly ShipmentCarrier Yyexpress = new("YYEXPRESS");

    /// <summary>
    /// Servientrega.
    /// </summary>
    public static readonly ShipmentCarrier Servientrega = new("SERVIENTREGA");

    /// <summary>
    /// HanJin.
    /// </summary>
    public static readonly ShipmentCarrier Hanjin = new("HANJIN");

    /// <summary>
    /// Spanish Seur.
    /// </summary>
    public static readonly ShipmentCarrier SpanishSeurFtp = new("SPANISH_SEUR_FTP");

    /// <summary>
    /// DX (B2B).
    /// </summary>
    public static readonly ShipmentCarrier DxB2BConnum = new("DX_B2B_CONNUM");

    /// <summary>
    /// Helthjem.
    /// </summary>
    public static readonly ShipmentCarrier HelthjemApi = new("HELTHJEM_API");

    /// <summary>
    /// Inexpost.
    /// </summary>
    public static readonly ShipmentCarrier Inexpost = new("INEXPOST");

    /// <summary>
    /// A2B Express Logistics.
    /// </summary>
    public static readonly ShipmentCarrier A2BBa = new("A2B_BA");

    /// <summary>
    /// Rhenus Logistics.
    /// </summary>
    public static readonly ShipmentCarrier RhenusGroup = new("RHENUS_GROUP");

    /// <summary>
    /// Sber Logistics.
    /// </summary>
    public static readonly ShipmentCarrier SberlogisticsRu = new("SBERLOGISTICS_RU");

    /// <summary>
    /// Malca-Amit.
    /// </summary>
    public static readonly ShipmentCarrier MalcaAmit = new("MALCA_AMIT");

    /// <summary>
    /// Professional Parcel Logistics.
    /// </summary>
    public static readonly ShipmentCarrier Ppl = new("PPL");

    /// <summary>
    /// OSM Worldwide.
    /// </summary>
    public static readonly ShipmentCarrier OsmWorldwideSftp = new("OSM_WORLDWIDE_SFTP");

    /// <summary>
    /// ACI Logistix.
    /// </summary>
    public static readonly ShipmentCarrier Acilogistix = new("ACILOGISTIX");

    /// <summary>
    /// Optima Courier.
    /// </summary>
    public static readonly ShipmentCarrier Optimacourier = new("OPTIMACOURIER");

    /// <summary>
    /// Nova Poshta API.
    /// </summary>
    public static readonly ShipmentCarrier NovaPoshtaApi = new("NOVA_POSHTA_API");

    /// <summary>
    /// Loggi.
    /// </summary>
    public static readonly ShipmentCarrier Loggi = new("LOGGI");

    /// <summary>
    /// YiFan Express.
    /// </summary>
    public static readonly ShipmentCarrier Yifan = new("YIFAN");

    /// <summary>
    /// My DynaLogic.
    /// </summary>
    public static readonly ShipmentCarrier Mydynalogic = new("MYDYNALOGIC");

    /// <summary>
    /// Morning Global.
    /// </summary>
    public static readonly ShipmentCarrier Morninglobal = new("MORNINGLOBAL");

    /// <summary>
    /// Concise.
    /// </summary>
    public static readonly ShipmentCarrier ConciseApi = new("CONCISE_API");

    /// <summary>
    /// Falcon Express.
    /// </summary>
    public static readonly ShipmentCarrier Fxtran = new("FXTRAN");

    /// <summary>
    /// Deliver Your Parcel.
    /// </summary>
    public static readonly ShipmentCarrier DeliveryourparcelZa = new("DELIVERYOURPARCEL_ZA");

    /// <summary>
    /// uParcel.
    /// </summary>
    public static readonly ShipmentCarrier Uparcel = new("UPARCEL");

    /// <summary>
    /// Mobi Logistica.
    /// </summary>
    public static readonly ShipmentCarrier MobiBr = new("MOBI_BR");

    /// <summary>
    /// T&amp;W Delivery.
    /// </summary>
    public static readonly ShipmentCarrier LoginextWebhook = new("LOGINEXT_WEBHOOK");

    /// <summary>
    /// EMS.
    /// </summary>
    public static readonly ShipmentCarrier Ems = new("EMS");

    /// <summary>
    /// Speedy.
    /// </summary>
    public static readonly ShipmentCarrier Speedy = new("SPEEDY");

    /// <summary>
    /// Zoom.
    /// </summary>
    public static readonly ShipmentCarrier ZoomRed = new("ZOOM_RED");

    /// <summary>
    /// Navlungo.
    /// </summary>
    public static readonly ShipmentCarrier Navlungo = new("NAVLUNGO");

    /// <summary>
    /// Castle Parcels.
    /// </summary>
    public static readonly ShipmentCarrier Castleparcels = new("CASTLEPARCELS");

    /// <summary>
    /// Weee.
    /// </summary>
    public static readonly ShipmentCarrier Weee = new("WEEE");

    /// <summary>
    /// Packaly.
    /// </summary>
    public static readonly ShipmentCarrier Packaly = new("PACKALY");

    /// <summary>
    /// Yunhuipost.
    /// </summary>
    public static readonly ShipmentCarrier Yunhuipost = new("YUNHUIPOST");

    /// <summary>
    /// YouParcel.
    /// </summary>
    public static readonly ShipmentCarrier Youparcel = new("YOUPARCEL");

    /// <summary>
    /// Leman.
    /// </summary>
    public static readonly ShipmentCarrier Leman = new("LEMAN");

    /// <summary>
    /// Moovin.
    /// </summary>
    public static readonly ShipmentCarrier Moovin = new("MOOVIN");

    /// <summary>
    /// Urb-it.
    /// </summary>
    public static readonly ShipmentCarrier UrbIt = new("URB_IT");

    /// <summary>
    /// Multientrega.
    /// </summary>
    public static readonly ShipmentCarrier Multientregapanama = new("MULTIENTREGAPANAMA");

    /// <summary>
    /// Jusdasr.
    /// </summary>
    public static readonly ShipmentCarrier Jusdasr = new("JUSDASR");

    /// <summary>
    /// Discount Post.
    /// </summary>
    public static readonly ShipmentCarrier Discountpost = new("DISCOUNTPOST");

    /// <summary>
    /// Rhenus Logistics UK.
    /// </summary>
    public static readonly ShipmentCarrier RhenusUk = new("RHENUS_UK");

    /// <summary>
    /// Swiship JP.
    /// </summary>
    public static readonly ShipmentCarrier SwishipJp = new("SWISHIP_JP");

    /// <summary>
    /// GLS USA.
    /// </summary>
    public static readonly ShipmentCarrier GlsUs = new("GLS_US");

    /// <summary>
    /// Southwestern Motor Transport. Inc.
    /// </summary>
    public static readonly ShipmentCarrier Smtl = new("SMTL");

    /// <summary>
    /// Discount Post Emega.
    /// </summary>
    public static readonly ShipmentCarrier Emega = new("EMEGA");

    /// <summary>
    /// EXPRESSONE Slovenia.
    /// </summary>
    public static readonly ShipmentCarrier ExpressoneSv = new("EXPRESSONE_SV");

    /// <summary>
    /// hepsiJET.
    /// </summary>
    public static readonly ShipmentCarrier Hepsijet = new("HEPSIJET");

    /// <summary>
    /// Welivery.
    /// </summary>
    public static readonly ShipmentCarrier Welivery = new("WELIVERY");

    /// <summary>
    /// Bringer Parcel Services.
    /// </summary>
    public static readonly ShipmentCarrier Bringer = new("BRINGER");

    /// <summary>
    /// EasyRoutes.
    /// </summary>
    public static readonly ShipmentCarrier Easyroutes = new("EASYROUTES");

    /// <summary>
    /// MRW.
    /// </summary>
    public static readonly ShipmentCarrier Mrw = new("MRW");

    /// <summary>
    /// RPM.
    /// </summary>
    public static readonly ShipmentCarrier Rpm = new("RPM");

    /// <summary>
    /// DPD Portugal.
    /// </summary>
    public static readonly ShipmentCarrier DpdPrt = new("DPD_PRT");

    /// <summary>
    /// GLS Romania.
    /// </summary>
    public static readonly ShipmentCarrier GlsRomania = new("GLS_ROMANIA");

    /// <summary>
    /// LM Parcel.
    /// </summary>
    public static readonly ShipmentCarrier Lmparcel = new("LMPARCEL");

    /// <summary>
    /// GTA GSM.
    /// </summary>
    public static readonly ShipmentCarrier Gtagsm = new("GTAGSM");

    /// <summary>
    /// DOMINO.
    /// </summary>
    public static readonly ShipmentCarrier Domino = new("DOMINO");

    /// <summary>
    /// eShipper.
    /// </summary>
    public static readonly ShipmentCarrier Eshipper = new("ESHIPPER");

    /// <summary>
    /// Transpak Inc..
    /// </summary>
    public static readonly ShipmentCarrier Transpak = new("TRANSPAK");

    /// <summary>
    /// Xindus.
    /// </summary>
    public static readonly ShipmentCarrier Xindus = new("XINDUS");

    /// <summary>
    /// Aoyue.
    /// </summary>
    public static readonly ShipmentCarrier Aoyue = new("AOYUE");

    /// <summary>
    /// Easyparcel.
    /// </summary>
    public static readonly ShipmentCarrier Easyparcel = new("EASYPARCEL");

    /// <summary>
    /// EXPRESSONE.
    /// </summary>
    public static readonly ShipmentCarrier Expressone = new("EXPRESSONE");

    /// <summary>
    /// Sendeo Kargo.
    /// </summary>
    public static readonly ShipmentCarrier SendeoKargo = new("SENDEO_KARGO");

    /// <summary>
    /// Speedaf Express.
    /// </summary>
    public static readonly ShipmentCarrier Speedaf = new("SPEEDAF");

    /// <summary>
    /// eTower.
    /// </summary>
    public static readonly ShipmentCarrier Etower = new("ETOWER");

    /// <summary>
    /// GC Express.
    /// </summary>
    public static readonly ShipmentCarrier Gcx = new("GCX");

    /// <summary>
    /// Ninjavan Vietnam.
    /// </summary>
    public static readonly ShipmentCarrier NinjavanVn = new("NINJAVAN_VN");

    /// <summary>
    /// Allegro.
    /// </summary>
    public static readonly ShipmentCarrier Allegro = new("ALLEGRO");

    /// <summary>
    /// Jumppoint.
    /// </summary>
    public static readonly ShipmentCarrier Jumppoint = new("JUMPPOINT");

    /// <summary>
    /// ShipGlobal.
    /// </summary>
    public static readonly ShipmentCarrier ShipglobalUs = new("SHIPGLOBAL_US");

    /// <summary>
    /// Kinisi Transport Pty Ltd.
    /// </summary>
    public static readonly ShipmentCarrier Kinisi = new("KINISI");

    /// <summary>
    /// Oakh Harbour Freight Lines.
    /// </summary>
    public static readonly ShipmentCarrier Oakh = new("OAKH");

    /// <summary>
    /// American West.
    /// </summary>
    public static readonly ShipmentCarrier Awest = new("AWEST");

    /// <summary>
    /// Barsan Global Lojistik.
    /// </summary>
    public static readonly ShipmentCarrier Barsan = new("BARSAN");

    /// <summary>
    /// Energo Logistic.
    /// </summary>
    public static readonly ShipmentCarrier Energologistic = new("ENERGOLOGISTIC");

    /// <summary>
    /// Madrooex.
    /// </summary>
    public static readonly ShipmentCarrier Madrooex = new("MADROOEX");

    /// <summary>
    /// GoBolt.
    /// </summary>
    public static readonly ShipmentCarrier Gobolt = new("GOBOLT");

    /// <summary>
    /// Swiss Universal Express.
    /// </summary>
    public static readonly ShipmentCarrier SwissUniversalExpress = new("SWISS_UNIVERSAL_EXPRESS");

    /// <summary>
    /// IOR Direct Solutions.
    /// </summary>
    public static readonly ShipmentCarrier Iordirect = new("IORDIRECT");

    /// <summary>
    /// xmszm.
    /// </summary>
    public static readonly ShipmentCarrier Xmszm = new("XMSZM");

    /// <summary>
    /// GLS Hungary.
    /// </summary>
    public static readonly ShipmentCarrier GlsHun = new("GLS_HUN");

    /// <summary>
    /// Sendy Express.
    /// </summary>
    public static readonly ShipmentCarrier Sendy = new("SENDY");

    /// <summary>
    /// Brauns Express.
    /// </summary>
    public static readonly ShipmentCarrier Braunsexpress = new("BRAUNSEXPRESS");

    /// <summary>
    /// Grand Slam Express.
    /// </summary>
    public static readonly ShipmentCarrier Grandslamexpress = new("GRANDSLAMEXPRESS");

    /// <summary>
    /// XGS.
    /// </summary>
    public static readonly ShipmentCarrier Xgs = new("XGS");

    /// <summary>
    /// OTS.
    /// </summary>
    public static readonly ShipmentCarrier Otschile = new("OTSCHILE");

    /// <summary>
    /// Pack-Up.
    /// </summary>
    public static readonly ShipmentCarrier PackUp = new("PACK_UP");

    /// <summary>
    /// Parcelstars.
    /// </summary>
    public static readonly ShipmentCarrier Parcelstars = new("PARCELSTARS");

    /// <summary>
    /// Team Express Service LLC.
    /// </summary>
    public static readonly ShipmentCarrier Teamexpressllc = new("TEAMEXPRESSLLC");

    /// <summary>
    /// Asyad Express.
    /// </summary>
    public static readonly ShipmentCarrier Asyadexpress = new("ASYADEXPRESS");

    /// <summary>
    /// TDN.
    /// </summary>
    public static readonly ShipmentCarrier Tdn = new("TDN");

    /// <summary>
    /// Early Bird.
    /// </summary>
    public static readonly ShipmentCarrier Earlybird = new("EARLYBIRD");

    /// <summary>
    /// Cacesa.
    /// </summary>
    public static readonly ShipmentCarrier Cacesa = new("CACESA");

    /// <summary>
    /// Parceljet.
    /// </summary>
    public static readonly ShipmentCarrier Parceljet = new("PARCELJET");

    /// <summary>
    /// MNG Kargo.
    /// </summary>
    public static readonly ShipmentCarrier MngKargo = new("MNG_KARGO");

    /// <summary>
    /// Super Pac Line.
    /// </summary>
    public static readonly ShipmentCarrier Superpackline = new("SUPERPACKLINE");

    /// <summary>
    /// SpeedX.
    /// </summary>
    public static readonly ShipmentCarrier Speedx = new("SPEEDX");

    /// <summary>
    /// Vesyl.
    /// </summary>
    public static readonly ShipmentCarrier Vesyl = new("VESYL");

    /// <summary>
    /// Sky King.
    /// </summary>
    public static readonly ShipmentCarrier Skyking = new("SKYKING");

    /// <summary>
    /// DIR.
    /// </summary>
    public static readonly ShipmentCarrier Dirmensajeria = new("DIRMENSAJERIA");

    /// <summary>
    /// Netlogix.
    /// </summary>
    public static readonly ShipmentCarrier Netlogixgroup = new("NETLOGIXGROUP");

    /// <summary>
    /// ZYEX.
    /// </summary>
    public static readonly ShipmentCarrier Zyou = new("ZYOU");

    /// <summary>
    /// Jawar.
    /// </summary>
    public static readonly ShipmentCarrier Jawar = new("JAWAR");

    /// <summary>
    /// Associate Global Systems.
    /// </summary>
    public static readonly ShipmentCarrier Agsystems = new("AGSYSTEMS");

    /// <summary>
    /// GPS.
    /// </summary>
    public static readonly ShipmentCarrier Gps = new("GPS");

    /// <summary>
    /// PTT Kargo.
    /// </summary>
    public static readonly ShipmentCarrier PttKargo = new("PTT_KARGO");

    /// <summary>
    /// Maergo.
    /// </summary>
    public static readonly ShipmentCarrier Maergo = new("MAERGO");

    /// <summary>
    /// AICS.
    /// </summary>
    public static readonly ShipmentCarrier Arihantcourier = new("ARIHANTCOURIER");

    /// <summary>
    /// VicTas Freight Express.
    /// </summary>
    public static readonly ShipmentCarrier Vtfe = new("VTFE");

    /// <summary>
    /// Yunant.
    /// </summary>
    public static readonly ShipmentCarrier Yunant = new("YUNANT");

    /// <summary>
    /// Urbify.
    /// </summary>
    public static readonly ShipmentCarrier Urbify = new("URBIFY");

    /// <summary>
    /// pack-man.
    /// </summary>
    public static readonly ShipmentCarrier PackMan = new("PACK_MAN");

    /// <summary>
    /// LIEFERGRUN.
    /// </summary>
    public static readonly ShipmentCarrier Liefergrun = new("LIEFERGRUN");

    /// <summary>
    /// Obibox.
    /// </summary>
    public static readonly ShipmentCarrier Obibox = new("OBIBOX");

    /// <summary>
    /// Paikeda.
    /// </summary>
    public static readonly ShipmentCarrier Paikeda = new("PAIKEDA");

    /// <summary>
    /// Scotty.
    /// </summary>
    public static readonly ShipmentCarrier Scotty = new("SCOTTY");

    /// <summary>
    /// Intelcom.
    /// </summary>
    public static readonly ShipmentCarrier IntelcomCa = new("INTELCOM_CA");

    /// <summary>
    /// swe.
    /// </summary>
    public static readonly ShipmentCarrier Swe = new("SWE");

    /// <summary>
    /// Asendia Global.
    /// </summary>
    public static readonly ShipmentCarrier Asendia = new("ASENDIA");

    /// <summary>
    /// DPD Austria.
    /// </summary>
    public static readonly ShipmentCarrier DpdAt = new("DPD_AT");

    /// <summary>
    /// Relay.
    /// </summary>
    public static readonly ShipmentCarrier Relay = new("RELAY");

    /// <summary>
    /// ATA.
    /// </summary>
    public static readonly ShipmentCarrier Ata = new("ATA");

    /// <summary>
    /// SkyExpress Internationals.
    /// </summary>
    public static readonly ShipmentCarrier SkyexpressInternational = new("SKYEXPRESS_INTERNATIONAL");

    /// <summary>
    /// Surat Kargo.
    /// </summary>
    public static readonly ShipmentCarrier SuratKargo = new("SURAT_KARGO");

    /// <summary>
    /// SG LINK.
    /// </summary>
    public static readonly ShipmentCarrier Sglink = new("SGLINK");

    /// <summary>
    /// FleetOptics.
    /// </summary>
    public static readonly ShipmentCarrier Fleetopticsinc = new("FLEETOPTICSINC");

    /// <summary>
    /// shopline.
    /// </summary>
    public static readonly ShipmentCarrier Shopline = new("SHOPLINE");

    /// <summary>
    /// PIGGYSHIP.
    /// </summary>
    public static readonly ShipmentCarrier Piggyship = new("PIGGYSHIP");

    /// <summary>
    /// LogoiX.
    /// </summary>
    public static readonly ShipmentCarrier Logoix = new("LOGOIX");

    /// <summary>
    /// Kolay Gelsin.
    /// </summary>
    public static readonly ShipmentCarrier KolayGelsin = new("KOLAY_GELSIN");

    /// <summary>
    /// Associated Couriers.
    /// </summary>
    public static readonly ShipmentCarrier AssociatedCouriers = new("ASSOCIATED_COURIERS");

    /// <summary>
    /// ups-checker.
    /// </summary>
    public static readonly ShipmentCarrier UpsChecker = new("UPS_CHECKER");

    /// <summary>
    /// Wineshipping.
    /// </summary>
    public static readonly ShipmentCarrier Wineshipping = new("WINESHIPPING");

    /// <summary>
    /// Spedisci online.
    /// </summary>
    public static readonly ShipmentCarrier Spedisci = new("SPEDISCI");

    /// <summary>
    /// Fourkites.
    /// </summary>
    public static readonly ShipmentCarrier Fourkites = new("FOURKITES");

    /// <summary>
    /// Etonas.
    /// </summary>
    public static readonly ShipmentCarrier Etonas = new("ETONAS");

    /// <summary>
    /// Fin Mile.
    /// </summary>
    public static readonly ShipmentCarrier Finmile = new("FINMILE");

    /// <summary>
    /// Uniuni.
    /// </summary>
    public static readonly ShipmentCarrier Uniuni = new("UNIUNI");

    /// <summary>
    /// Rodonaves.
    /// </summary>
    public static readonly ShipmentCarrier Rodonaves = new("RODONAVES");

    /// <summary>
    /// Inpost Italy.
    /// </summary>
    public static readonly ShipmentCarrier InpostIt = new("INPOST_IT");

    /// <summary>
    /// Tforce Freight.
    /// </summary>
    public static readonly ShipmentCarrier TforceFreight = new("TFORCE_FREIGHT");

    /// <summary>
    /// Rich Mom.
    /// </summary>
    public static readonly ShipmentCarrier Richmom = new("RICHMOM");

    /// <summary>
    /// Corriere Franco.
    /// </summary>
    public static readonly ShipmentCarrier Franco = new("FRANCO");

    /// <summary>
    /// Ecparcel.
    /// </summary>
    public static readonly ShipmentCarrier Ecparcel = new("ECPARCEL");

    /// <summary>
    /// Fedex China.
    /// </summary>
    public static readonly ShipmentCarrier FedexChina = new("FEDEX_CHINA");

    /// <summary>
    /// Gofo Express.
    /// </summary>
    public static readonly ShipmentCarrier GofoExpress = new("GOFO_EXPRESS");

    /// <summary>
    /// Shipbob.
    /// </summary>
    public static readonly ShipmentCarrier Shipbob = new("SHIPBOB");

    /// <summary>
    /// Jersey Post Group.
    /// </summary>
    public static readonly ShipmentCarrier JerseypostAtlas = new("JERSEYPOST_ATLAS");

    /// <summary>
    /// Coretrails.
    /// </summary>
    public static readonly ShipmentCarrier Coretrails = new("CORETRAILS");

    /// <summary>
    /// Rhenus Logistics Italy.
    /// </summary>
    public static readonly ShipmentCarrier RhenusItaly = new("RHENUS_ITALY");

    /// <summary>
    /// Jadlog.
    /// </summary>
    public static readonly ShipmentCarrier Jadlog = new("JADLOG");

    /// <summary>
    /// Jitsu.
    /// </summary>
    public static readonly ShipmentCarrier Jitsu = new("JITSU");

    /// <summary>
    /// Yanwen Express.
    /// </summary>
    public static readonly ShipmentCarrier YanwenExpress = new("YANWEN_EXPRESS");

    /// <summary>
    /// Dashlink.
    /// </summary>
    public static readonly ShipmentCarrier Dashlink = new("DASHLINK");

    /// <summary>
    /// Seino Super Express.
    /// </summary>
    public static readonly ShipmentCarrier SeinoSuperExpress = new("SEINO_SUPER_EXPRESS");

    /// <summary>
    /// Floship.
    /// </summary>
    public static readonly ShipmentCarrier Floship = new("FLOSHIP");

    /// <summary>
    /// Metro Supply Chain.
    /// </summary>
    public static readonly ShipmentCarrier Metroscg = new("METROSCG");

    /// <summary>
    /// Sendparcel.
    /// </summary>
    public static readonly ShipmentCarrier Sendparcel = new("SENDPARCEL");

    /// <summary>
    /// P2p.
    /// </summary>
    public static readonly ShipmentCarrier P2P = new("P2P");

    /// <summary>
    /// Cn Express.
    /// </summary>
    public static readonly ShipmentCarrier CnExpress = new("CN_EXPRESS");

    /// <summary>
    /// Cirro Track.
    /// </summary>
    public static readonly ShipmentCarrier Cirrotrack = new("CIRROTRACK");

    /// <summary>
    /// Land Logistics.
    /// </summary>
    public static readonly ShipmentCarrier LandLogistics = new("LAND_LOGISTICS");

    /// <summary>
    /// Veho.
    /// </summary>
    public static readonly ShipmentCarrier Veho = new("VEHO");

    /// <summary>
    /// Medline.
    /// </summary>
    public static readonly ShipmentCarrier Medline = new("MEDLINE");

    /// <summary>
    /// Vdtrack.
    /// </summary>
    public static readonly ShipmentCarrier Vdtrack = new("VDTRACK");

    /// <summary>
    /// Sino Scm.
    /// </summary>
    public static readonly ShipmentCarrier SinoScm = new("SINO_SCM");

    /// <summary>
    /// 3pe Express.
    /// </summary>
    public static readonly ShipmentCarrier _3PeExpress = new("3PE_EXPRESS");

    /// <summary>
    /// Swiftx.
    /// </summary>
    public static readonly ShipmentCarrier Swiftx = new("SWIFTX");

    /// <summary>
    /// Sfyd Express.
    /// </summary>
    public static readonly ShipmentCarrier Sfydexpress = new("SFYDEXPRESS");

    /// <summary>
    /// Toptrans.
    /// </summary>
    public static readonly ShipmentCarrier Toptrans = new("TOPTRANS");

    /// <summary>
    /// Other.
    /// </summary>
    public static readonly ShipmentCarrier Other = new("OTHER");

    public TResult Match<TResult>(Func<TResult> onDpdRu,
        Func<TResult> onBgBulgarianPost,
        Func<TResult> onKrKoreaPost,
        Func<TResult> onZaCourierit,
        Func<TResult> onFrExapaq,
        Func<TResult> onAreEmiratesPost,
        Func<TResult> onGac,
        Func<TResult> onGeis,
        Func<TResult> onSfEx,
        Func<TResult> onPago,
        Func<TResult> onMyhermes,
        Func<TResult> onDiamondEurogistics,
        Func<TResult> onCorporatecouriersWebhook,
        Func<TResult> onBond,
        Func<TResult> onOmniparcel,
        Func<TResult> onSkPosta,
        Func<TResult> onPurolator,
        Func<TResult> onFetchrWebhook,
        Func<TResult> onThedeliverygroup,
        Func<TResult> onCelloSquare,
        Func<TResult> onTarrive,
        Func<TResult> onCollivery,
        Func<TResult> onMainfreight,
        Func<TResult> onIndFirstflight,
        Func<TResult> onAcsworldwide,
        Func<TResult> onAmstan,
        Func<TResult> onOkayparcel,
        Func<TResult> onEnvialiaReference,
        Func<TResult> onSeurEs,
        Func<TResult> onContinental,
        Func<TResult> onFdsexpress,
        Func<TResult> onAmazonFbaSwiship,
        Func<TResult> onWyngs,
        Func<TResult> onDhlActiveTracing,
        Func<TResult> onZyllem,
        Func<TResult> onRuston,
        Func<TResult> onXpost,
        Func<TResult> onCorreosEs,
        Func<TResult> onDhlFr,
        Func<TResult> onPanAsia,
        Func<TResult> onBrtIt,
        Func<TResult> onSreKorea,
        Func<TResult> onSpeedee,
        Func<TResult> onTntUk,
        Func<TResult> onVenipak,
        Func<TResult> onShreenandancourier,
        Func<TResult> onCroshot,
        Func<TResult> onNipostNg,
        Func<TResult> onEpstGlbl,
        Func<TResult> onNewgistics,
        Func<TResult> onPostSlovenia,
        Func<TResult> onJerseyPost,
        Func<TResult> onBombinoexp,
        Func<TResult> onWmg,
        Func<TResult> onXqExpress,
        Func<TResult> onFurdeco,
        Func<TResult> onLhtExpress,
        Func<TResult> onSouthAfricanPostOffice,
        Func<TResult> onSpoton,
        Func<TResult> onDimerco,
        Func<TResult> onCyprusPostCyp,
        Func<TResult> onAbcustom,
        Func<TResult> onIndDelivree,
        Func<TResult> onCnBestexpress,
        Func<TResult> onDxSftp,
        Func<TResult> onPickuppMys,
        Func<TResult> onFmx,
        Func<TResult> onHellmann,
        Func<TResult> onShipItAsia,
        Func<TResult> onKerryEcommerce,
        Func<TResult> onFreterapido,
        Func<TResult> onPitneyBowes,
        Func<TResult> onXpressenDk,
        Func<TResult> onSeurSpApi,
        Func<TResult> onDeliveryontime,
        Func<TResult> onJinsung,
        Func<TResult> onTransKargo,
        Func<TResult> onSwishipDe,
        Func<TResult> onIvoyWebhook,
        Func<TResult> onAirmeeWebhook,
        Func<TResult> onDhlBenelux,
        Func<TResult> onFirstmile,
        Func<TResult> onFastwayIr,
        Func<TResult> onHhExp,
        Func<TResult> onMysMypostOnline,
        Func<TResult> onTntNl,
        Func<TResult> onTipsa,
        Func<TResult> onTaqbinMy,
        Func<TResult> onKgmhub,
        Func<TResult> onIntexpress,
        Func<TResult> onOverseExp,
        Func<TResult> onOneclick,
        Func<TResult> onRoadrunnerFreight,
        Func<TResult> onGlsCrotia,
        Func<TResult> onMrwFtp,
        Func<TResult> onBluex,
        Func<TResult> onDylt,
        Func<TResult> onDpdIr,
        Func<TResult> onSinGlbl,
        Func<TResult> onTuffnellsReference,
        Func<TResult> onCjpacket,
        Func<TResult> onMilkman,
        Func<TResult> onAsigna,
        Func<TResult> onOneworldexpress,
        Func<TResult> onRoyalMail,
        Func<TResult> onViaExpress,
        Func<TResult> onTigfreight,
        Func<TResult> onZtoExpress,
        Func<TResult> onTwoGo,
        Func<TResult> onIml,
        Func<TResult> onIntelValley,
        Func<TResult> onEfs,
        Func<TResult> onUkUkMail,
        Func<TResult> onRam,
        Func<TResult> onAlliedexpress,
        Func<TResult> onApcOvernight,
        Func<TResult> onShippit,
        Func<TResult> onTfm,
        Func<TResult> onMXpress,
        Func<TResult> onHdbBox,
        Func<TResult> onClevyLinks,
        Func<TResult> onIbeone,
        Func<TResult> onFiegeNl,
        Func<TResult> onKweGlobal,
        Func<TResult> onCtcExpress,
        Func<TResult> onAmazon,
        Func<TResult> onMoreLink,
        Func<TResult> onJx,
        Func<TResult> onEasyMail,
        Func<TResult> onAduiepyle,
        Func<TResult> onGbPanther,
        Func<TResult> onExpresssale,
        Func<TResult> onSgDetrack,
        Func<TResult> onTrunkrsWebhook,
        Func<TResult> onMatdespatch,
        Func<TResult> onDicom,
        Func<TResult> onMbw,
        Func<TResult> onKhmCambodiaPost,
        Func<TResult> onSinotrans,
        Func<TResult> onBrtItParcelid,
        Func<TResult> onDhlSupplyChain,
        Func<TResult> onDhlPl,
        Func<TResult> onTopyou,
        Func<TResult> onPalexpress,
        Func<TResult> onDhlSg,
        Func<TResult> onCnWedo,
        Func<TResult> onFulfillme,
        Func<TResult> onDpdDelistrack,
        Func<TResult> onUpsReference,
        Func<TResult> onCaribou,
        Func<TResult> onLocusWebhook,
        Func<TResult> onDsv,
        Func<TResult> onP2PTrc,
        Func<TResult> onDirectparcels,
        Func<TResult> onNovaPoshtaInt,
        Func<TResult> onFedexPoland,
        Func<TResult> onCnJcex,
        Func<TResult> onFarInternational,
        Func<TResult> onIdexpress,
        Func<TResult> onGangbao,
        Func<TResult> onNeway,
        Func<TResult> onPostnlInt3S,
        Func<TResult> onRpxId,
        Func<TResult> onDesignertransportWebhook,
        Func<TResult> onGlsSloven,
        Func<TResult> onParcelledIn,
        Func<TResult> onGsiExpress,
        Func<TResult> onConWay,
        Func<TResult> onBrouwerTransport,
        Func<TResult> onCpex,
        Func<TResult> onIsraelPost,
        Func<TResult> onDtdcIn,
        Func<TResult> onPttPost,
        Func<TResult> onXdeWebhook,
        Func<TResult> onTolos,
        Func<TResult> onGiaoHang,
        Func<TResult> onGeodisEspace,
        Func<TResult> onMagyarHu,
        Func<TResult> onDoordashWebhook,
        Func<TResult> onTikiId,
        Func<TResult> onCjHkInternational,
        Func<TResult> onStarTrackExpress,
        Func<TResult> onHelthjem,
        Func<TResult> onSfb2C,
        Func<TResult> onFreightquote,
        Func<TResult> onLandmarkGlobalReference,
        Func<TResult> onParcel2Go,
        Func<TResult> onDelnext,
        Func<TResult> onRcl,
        Func<TResult> onCgsExpress,
        Func<TResult> onHkPost,
        Func<TResult> onSapExpress,
        Func<TResult> onParcelpostSg,
        Func<TResult> onHermes,
        Func<TResult> onIndSafeexpress,
        Func<TResult> onTophatterexpress,
        Func<TResult> onMglobal,
        Func<TResult> onAveritt,
        Func<TResult> onLeader,
        Func<TResult> on_2Ebox,
        Func<TResult> onSgSpeedpost,
        Func<TResult> onDbschenkerSe,
        Func<TResult> onIsrPostDomestic,
        Func<TResult> onBestwayparcel,
        Func<TResult> onAsendiaDe,
        Func<TResult> onNightlineUk,
        Func<TResult> onTaqbinSg,
        Func<TResult> onTckExpress,
        Func<TResult> onEndeavourDelivery,
        Func<TResult> onNanjingwoyuan,
        Func<TResult> onHeppnerFr,
        Func<TResult> onEmpsCn,
        Func<TResult> onFonsen,
        Func<TResult> onPickrr,
        Func<TResult> onApcOvernightConnum,
        Func<TResult> onStarTrackNextFlight,
        Func<TResult> onDajin,
        Func<TResult> onUpsFreight,
        Func<TResult> onPostaPlus,
        Func<TResult> onCeva,
        Func<TResult> onAnserx,
        Func<TResult> onJsExpress,
        Func<TResult> onPadtf,
        Func<TResult> onUpsMailInnovations,
        Func<TResult> onSypost,
        Func<TResult> onAmazonShipMcf,
        Func<TResult> onYusen,
        Func<TResult> onBring,
        Func<TResult> onSdaIt,
        Func<TResult> onGba,
        Func<TResult> onNeweggexpress,
        Func<TResult> onSpeedcouriersGr,
        Func<TResult> onForrun,
        Func<TResult> onPickup,
        Func<TResult> onEcms,
        Func<TResult> onIntelipost,
        Func<TResult> onFlashexpress,
        Func<TResult> onCnSto,
        Func<TResult> onSekoSftp,
        Func<TResult> onHomeDeliverySolutions,
        Func<TResult> onDpdHgry,
        Func<TResult> onKerryttcVn,
        Func<TResult> onJoyingBox,
        Func<TResult> onTotalExpress,
        Func<TResult> onZjsExpress,
        Func<TResult> onStarken,
        Func<TResult> onDemandship,
        Func<TResult> onCnDpex,
        Func<TResult> onAupostCn,
        Func<TResult> onLogisters,
        Func<TResult> onGoglobalpost,
        Func<TResult> onGlsCz,
        Func<TResult> onPaackWebhook,
        Func<TResult> onGrabWebhook,
        Func<TResult> onParcelpoint,
        Func<TResult> onIcumulus,
        Func<TResult> onDaiglobaltrack,
        Func<TResult> onGlobalIparcel,
        Func<TResult> onYurticiKargo,
        Func<TResult> onCnPaypalPackage,
        Func<TResult> onParcel2Post,
        Func<TResult> onGlsIt,
        Func<TResult> onPilLogistics,
        Func<TResult> onHeppner,
        Func<TResult> onGeneralOvernight,
        Func<TResult> onHappy2Point,
        Func<TResult> onChitchats,
        Func<TResult> onSmooth,
        Func<TResult> onCleLogistics,
        Func<TResult> onFiege,
        Func<TResult> onMxCargo,
        Func<TResult> onZiingfinalmile,
        Func<TResult> onDaytonFreight,
        Func<TResult> onTcs,
        Func<TResult> onAex,
        Func<TResult> onHermesDe,
        Func<TResult> onRoutificWebhook,
        Func<TResult> onGlobavend,
        Func<TResult> onCjLogistics,
        Func<TResult> onPalletNetwork,
        Func<TResult> onRafPh,
        Func<TResult> onUkXdp,
        Func<TResult> onPaperExpress,
        Func<TResult> onLaPosteSuivi,
        Func<TResult> onPaquetexpress,
        Func<TResult> onLiefery,
        Func<TResult> onStreckTransport,
        Func<TResult> onPonyExpress,
        Func<TResult> onAlwaysExpress,
        Func<TResult> onGbsBroker,
        Func<TResult> onCitylinkMy,
        Func<TResult> onAlljoy,
        Func<TResult> onYodel,
        Func<TResult> onYodelDir,
        Func<TResult> onStone3Pl,
        Func<TResult> onParcelpalWebhook,
        Func<TResult> onDhlEcomerceAsa,
        Func<TResult> onSimplypost,
        Func<TResult> onKyExpress,
        Func<TResult> onShenzhen,
        Func<TResult> onUsLasership,
        Func<TResult> onUcExpre,
        Func<TResult> onDidadi,
        Func<TResult> onCjKr,
        Func<TResult> onDbschenkerB2B,
        Func<TResult> onMxe,
        Func<TResult> onCaeDelivers,
        Func<TResult> onPfcexpress,
        Func<TResult> onWhistl,
        Func<TResult> onWepost,
        Func<TResult> onDhlParcelEs,
        Func<TResult> onDdexpress,
        Func<TResult> onAramexAu,
        Func<TResult> onBneed,
        Func<TResult> onHkTgx,
        Func<TResult> onLatvijasPasts,
        Func<TResult> onViaeurope,
        Func<TResult> onCorreoUy,
        Func<TResult> onChronopostFr,
        Func<TResult> onJNet,
        Func<TResult> on_6Ls,
        Func<TResult> onBlrBelpost,
        Func<TResult> onBirdsystem,
        Func<TResult> onDobropost,
        Func<TResult> onWahanaId,
        Func<TResult> onWeaship,
        Func<TResult> onSonictl,
        Func<TResult> onKwt,
        Func<TResult> onAfllogFtp,
        Func<TResult> onSkynetWorldwide,
        Func<TResult> onNovaPoshta,
        Func<TResult> onSeino,
        Func<TResult> onSzendex,
        Func<TResult> onBpostInt,
        Func<TResult> onDbschenkerSv,
        Func<TResult> onAoDeutschland,
        Func<TResult> onEuFleetSolutions,
        Func<TResult> onPcfcorp,
        Func<TResult> onLinkbridge,
        Func<TResult> onPrimamulticipta,
        Func<TResult> onCourex,
        Func<TResult> onZajilExpress,
        Func<TResult> onCollectco,
        Func<TResult> onJtexpress,
        Func<TResult> onFedexUk,
        Func<TResult> onUship,
        Func<TResult> onPixsell,
        Func<TResult> onShiptor,
        Func<TResult> onCdek,
        Func<TResult> onVnmViettelpost,
        Func<TResult> onCjCentury,
        Func<TResult> onGso,
        Func<TResult> onViwo,
        Func<TResult> onSkybox,
        Func<TResult> onKerrytj,
        Func<TResult> onNtlogisticsVn,
        Func<TResult> onSdhScm,
        Func<TResult> onZinc,
        Func<TResult> onDpeSouthAfrc,
        Func<TResult> onCeskaCz,
        Func<TResult> onAcsGr,
        Func<TResult> onDealersend,
        Func<TResult> onJocom,
        Func<TResult> onCse,
        Func<TResult> onTforceFinalmile,
        Func<TResult> onShipGate,
        Func<TResult> onShipter,
        Func<TResult> onNationalSameday,
        Func<TResult> onYunexpress,
        Func<TResult> onCainiao,
        Func<TResult> onDmsMatrix,
        Func<TResult> onDirectlog,
        Func<TResult> onAsendiaUs,
        Func<TResult> on_3Jmslogistics,
        Func<TResult> onLiccardiExpress,
        Func<TResult> onSkyPostal,
        Func<TResult> onCnwangtong,
        Func<TResult> onPostnordLogisticsDk,
        Func<TResult> onLogistika,
        Func<TResult> onCeleritas,
        Func<TResult> onPressiode,
        Func<TResult> onShreeMaruti,
        Func<TResult> onLogisticsworldwideHk,
        Func<TResult> onEfex,
        Func<TResult> onLotte,
        Func<TResult> onLonestar,
        Func<TResult> onAprisaexpress,
        Func<TResult> onBelRs,
        Func<TResult> onOsmWorldwide,
        Func<TResult> onWestgateGl,
        Func<TResult> onFastrack,
        Func<TResult> onDtdExpr,
        Func<TResult> onAlfatrex,
        Func<TResult> onPromeddelivery,
        Func<TResult> onThabitLogistics,
        Func<TResult> onHctLogistics,
        Func<TResult> onCarryFlap,
        Func<TResult> onUsOldDominion,
        Func<TResult> onAnicamBox,
        Func<TResult> onWanbexpress,
        Func<TResult> onAnPost,
        Func<TResult> onDpdLocal,
        Func<TResult> onStallionexpress,
        Func<TResult> onRaiderex,
        Func<TResult> onShopfans,
        Func<TResult> onKyungdongParcel,
        Func<TResult> onChampionLogistics,
        Func<TResult> onPickuppSgp,
        Func<TResult> onMorningExpress,
        Func<TResult> onNacex,
        Func<TResult> onThenileWebhook,
        Func<TResult> onHolisol,
        Func<TResult> onLbcexpressFtp,
        Func<TResult> onKurasi,
        Func<TResult> onUsfReddaway,
        Func<TResult> onApg,
        Func<TResult> onCnBoxc,
        Func<TResult> onEcoscooting,
        Func<TResult> onMainway,
        Func<TResult> onPaperfly,
        Func<TResult> onHoundexpress,
        Func<TResult> onBoxBerry,
        Func<TResult> onEpBox,
        Func<TResult> onPlusLogUk,
        Func<TResult> onFulfilla,
        Func<TResult> onAse,
        Func<TResult> onMailPlus,
        Func<TResult> onXpoLogistics,
        Func<TResult> onWndirect,
        Func<TResult> onCloudwishAsia,
        Func<TResult> onZeleris,
        Func<TResult> onGioExpress,
        Func<TResult> onOcsWorldwide,
        Func<TResult> onArkLogistics,
        Func<TResult> onAquiline,
        Func<TResult> onPilotFreight,
        Func<TResult> onQwintry,
        Func<TResult> onDanskeFragt,
        Func<TResult> onCarriers,
        Func<TResult> onAirCanadaGlobal,
        Func<TResult> onPresidentTrans,
        Func<TResult> onStepforwardfs,
        Func<TResult> onSkynetUk,
        Func<TResult> onPittohio,
        Func<TResult> onCorreosExpress,
        Func<TResult> onRlUs,
        Func<TResult> onDestiny,
        Func<TResult> onUkYodel,
        Func<TResult> onCometTech,
        Func<TResult> onDhlParcelRu,
        Func<TResult> onTntRefr,
        Func<TResult> onShreeAnjaniCourier,
        Func<TResult> onMikropakketBe,
        Func<TResult> onEtsExpress,
        Func<TResult> onColisPrive,
        Func<TResult> onCnYunda,
        Func<TResult> onAaaCooper,
        Func<TResult> onRocketParcel,
        Func<TResult> on_360Lion,
        Func<TResult> onPandu,
        Func<TResult> onProfessionalCouriers,
        Func<TResult> onFlytexpress,
        Func<TResult> onLogisticsworldwideMy,
        Func<TResult> onCorreosDeEspana,
        Func<TResult> onImx,
        Func<TResult> onFourPxExpress,
        Func<TResult> onXpressbees,
        Func<TResult> onPickuppVnm,
        Func<TResult> onStartrackExpress,
        Func<TResult> onFrColissimo,
        Func<TResult> onNacexSpainReference,
        Func<TResult> onDhlSupplyChainAu,
        Func<TResult> onEshipping,
        Func<TResult> onShreetirupati,
        Func<TResult> onHxExpress,
        Func<TResult> onIndopaket,
        Func<TResult> onCn17Post,
        Func<TResult> onK1Express,
        Func<TResult> onCjGls,
        Func<TResult> onMysGdex,
        Func<TResult> onNationex,
        Func<TResult> onAnjun,
        Func<TResult> onFargood,
        Func<TResult> onSmgExpress,
        Func<TResult> onRzyexpress,
        Func<TResult> onSefl,
        Func<TResult> onTntClickIt,
        Func<TResult> onHdb,
        Func<TResult> onHipshipper,
        Func<TResult> onRpxlogistics,
        Func<TResult> onKuehne,
        Func<TResult> onItNexive,
        Func<TResult> onPts,
        Func<TResult> onSwissPostFtp,
        Func<TResult> onFastrkServ,
        Func<TResult> on_472,
        Func<TResult> onUsYrc,
        Func<TResult> onPostnlIntl3S,
        Func<TResult> onElianPost,
        Func<TResult> onCubyn,
        Func<TResult> onSauSaudiPost,
        Func<TResult> onAbxexpressMy,
        Func<TResult> onHuahanExpress,
        Func<TResult> onZesExpress,
        Func<TResult> onZeptoExpress,
        Func<TResult> onSkynetZa,
        Func<TResult> onZeek2Door,
        Func<TResult> onBlinklastmile,
        Func<TResult> onPostaUkr,
        Func<TResult> onChrobinson,
        Func<TResult> onCnPost56,
        Func<TResult> onCourantPlus,
        Func<TResult> onScudexExpress,
        Func<TResult> onShipentegra,
        Func<TResult> onBTwoCEurope,
        Func<TResult> onCope,
        Func<TResult> onIndGati,
        Func<TResult> onCnWishpost,
        Func<TResult> onNacexEs,
        Func<TResult> onTaqbinHk,
        Func<TResult> onGlobaltranz,
        Func<TResult> onHkd,
        Func<TResult> onBjshomedelivery,
        Func<TResult> onOmniva,
        Func<TResult> onSutton,
        Func<TResult> onPantherReference,
        Func<TResult> onSfcservice,
        Func<TResult> onLtl,
        Func<TResult> onParknparcel,
        Func<TResult> onSpringGds,
        Func<TResult> onEcexpress,
        Func<TResult> onInterparcelAu,
        Func<TResult> onAgility,
        Func<TResult> onXlExpress,
        Func<TResult> onAderonline,
        Func<TResult> onDirectcouriers,
        Func<TResult> onPlanzer,
        Func<TResult> onSending,
        Func<TResult> onNinjavanWb,
        Func<TResult> onNationwideMy,
        Func<TResult> onSendit,
        Func<TResult> onGbArrow,
        Func<TResult> onIndGojavas,
        Func<TResult> onKpost,
        Func<TResult> onDhlFreight,
        Func<TResult> onBluecare,
        Func<TResult> onJindouyun,
        Func<TResult> onTrackon,
        Func<TResult> onGbTuffnells,
        Func<TResult> onTrumpcard,
        Func<TResult> onEtotal,
        Func<TResult> onSfplusWebhook,
        Func<TResult> onSekologistics,
        Func<TResult> onHermes2MannHandling,
        Func<TResult> onDpdLocalRef,
        Func<TResult> onUds,
        Func<TResult> onZaSpecialisedFreight,
        Func<TResult> onThaKerry,
        Func<TResult> onPrtIntSeur,
        Func<TResult> onBraCorreios,
        Func<TResult> onNzNzPost,
        Func<TResult> onCnEquick,
        Func<TResult> onMysEms,
        Func<TResult> onGbNorsk,
        Func<TResult> onEspMrw,
        Func<TResult> onEspPacklink,
        Func<TResult> onKangarooMy,
        Func<TResult> onRpx,
        Func<TResult> onXdpUkReference,
        Func<TResult> onNinjavanMy,
        Func<TResult> onAdicional,
        Func<TResult> onRoadbull,
        Func<TResult> onYakit,
        Func<TResult> onMailamericas,
        Func<TResult> onMikropakket,
        Func<TResult> onDynalogic,
        Func<TResult> onDhlEs,
        Func<TResult> onDhlParcelNl,
        Func<TResult> onDhlGlobalMailAsia,
        Func<TResult> onDawnWing,
        Func<TResult> onGenikiGr,
        Func<TResult> onHermesworldUk,
        Func<TResult> onAlphafast,
        Func<TResult> onBuylogic,
        Func<TResult> onEkart,
        Func<TResult> onMexSenda,
        Func<TResult> onSfcLogistics,
        Func<TResult> onPostSerbia,
        Func<TResult> onIndDelhivery,
        Func<TResult> onDeDpdDelistrack,
        Func<TResult> onRpd2Man,
        Func<TResult> onCnSfExpress,
        Func<TResult> onYanwen,
        Func<TResult> onMysSkynet,
        Func<TResult> onCorreosDeMexico,
        Func<TResult> onCblLogistica,
        Func<TResult> onMexEstafeta,
        Func<TResult> onAuAustrianPost,
        Func<TResult> onRincos,
        Func<TResult> onNldDhl,
        Func<TResult> onRussianPost,
        Func<TResult> onCouriersPlease,
        Func<TResult> onPostnordLogistics,
        Func<TResult> onFedex,
        Func<TResult> onDpeExpress,
        Func<TResult> onDpd,
        Func<TResult> onAdsone,
        Func<TResult> onIdnJne,
        Func<TResult> onThecourierguy,
        Func<TResult> onCnexps,
        Func<TResult> onPrtChronopost,
        Func<TResult> onLandmarkGlobal,
        Func<TResult> onItDhlEcommerce,
        Func<TResult> onEspNacex,
        Func<TResult> onPrtCtt,
        Func<TResult> onBeKiala,
        Func<TResult> onAsendiaUk,
        Func<TResult> onGlobalTnt,
        Func<TResult> onPosturIs,
        Func<TResult> onEparcelKr,
        Func<TResult> onInpostPaczkomaty,
        Func<TResult> onItPosteItalia,
        Func<TResult> onBeBpost,
        Func<TResult> onPlPocztaPolska,
        Func<TResult> onMysMysPost,
        Func<TResult> onSgSgPost,
        Func<TResult> onThaThailandPost,
        Func<TResult> onLexship,
        Func<TResult> onFastwayNz,
        Func<TResult> onDhlAu,
        Func<TResult> onCostmeticsnow,
        Func<TResult> onPflogistics,
        Func<TResult> onLoomisExpress,
        Func<TResult> onGlsItaly,
        Func<TResult> onLine,
        Func<TResult> onGelExpress,
        Func<TResult> onHuodull,
        Func<TResult> onNinjavanSg,
        Func<TResult> onJanio,
        Func<TResult> onAoCourier,
        Func<TResult> onBrtItSenderRef,
        Func<TResult> onSailpost,
        Func<TResult> onLalamove,
        Func<TResult> onNewzealandCouriers,
        Func<TResult> onEtomars,
        Func<TResult> onVirtransport,
        Func<TResult> onWizmo,
        Func<TResult> onPalletways,
        Func<TResult> onIDika,
        Func<TResult> onCflLogistics,
        Func<TResult> onGemworldwide,
        Func<TResult> onGlobalExpress,
        Func<TResult> onLogistyxTransgroup,
        Func<TResult> onWestbankCourier,
        Func<TResult> onArcoSpedizioni,
        Func<TResult> onYdhExpress,
        Func<TResult> onParcelinklogistics,
        Func<TResult> onCndexpress,
        Func<TResult> onNoxNightTimeExpress,
        Func<TResult> onAeronet,
        Func<TResult> onLtianexp,
        Func<TResult> onIntegra2Ftp,
        Func<TResult> onParcelone,
        Func<TResult> onNoxNachtexpress,
        Func<TResult> onCnChinaPostEms,
        Func<TResult> onChukou1,
        Func<TResult> onGlsSlov,
        Func<TResult> onOrangeDs,
        Func<TResult> onJoomLogis,
        Func<TResult> onAusStartrack,
        Func<TResult> onDhl,
        Func<TResult> onGbApc,
        Func<TResult> onBondscouriers,
        Func<TResult> onJpnJapanPost,
        Func<TResult> onUsps,
        Func<TResult> onWinit,
        Func<TResult> onArgOca,
        Func<TResult> onTwTaiwanPost,
        Func<TResult> onDmmNetwork,
        Func<TResult> onTnt,
        Func<TResult> onBhPosta,
        Func<TResult> onSwePostnord,
        Func<TResult> onCaCanadaPost,
        Func<TResult> onWiseloads,
        Func<TResult> onAsendiaHk,
        Func<TResult> onNldGls,
        Func<TResult> onMexRedpack,
        Func<TResult> onJetShip,
        Func<TResult> onDeDhlExpress,
        Func<TResult> onNinjavanThai,
        Func<TResult> onRabenGroup,
        Func<TResult> onEspAsm,
        Func<TResult> onHrvHrvatska,
        Func<TResult> onGlobalEstes,
        Func<TResult> onLtuLietuvos,
        Func<TResult> onBelDhl,
        Func<TResult> onAuAuPost,
        Func<TResult> onSpeedexcourier,
        Func<TResult> onFrColis,
        Func<TResult> onAramex,
        Func<TResult> onDpex,
        Func<TResult> onMysAirpak,
        Func<TResult> onCuckooexpress,
        Func<TResult> onDpdPoland,
        Func<TResult> onNldPostnl,
        Func<TResult> onNimExpress,
        Func<TResult> onQuantium,
        Func<TResult> onSendle,
        Func<TResult> onEspRedur,
        Func<TResult> onMatkahuolto,
        Func<TResult> onCpacket,
        Func<TResult> onPosti,
        Func<TResult> onHunterExpress,
        Func<TResult> onChoirExp,
        Func<TResult> onLegionExpress,
        Func<TResult> onAustrianPostExpress,
        Func<TResult> onGrupo,
        Func<TResult> onPostaRo,
        Func<TResult> onInterparcelUk,
        Func<TResult> onGlobalAbf,
        Func<TResult> onPostenNorge,
        Func<TResult> onXpertDelivery,
        Func<TResult> onDhlRefr,
        Func<TResult> onDhlHk,
        Func<TResult> onSkynetUae,
        Func<TResult> onGojek,
        Func<TResult> onYodelIntnl,
        Func<TResult> onJanco,
        Func<TResult> onYto,
        Func<TResult> onWiseExpress,
        Func<TResult> onJtexpressVn,
        Func<TResult> onFedexIntlMlserv,
        Func<TResult> onVamox,
        Func<TResult> onAmsGrp,
        Func<TResult> onDhlJp,
        Func<TResult> onHrparcel,
        Func<TResult> onGeswl,
        Func<TResult> onBluestar,
        Func<TResult> onCdekTr,
        Func<TResult> onDescartes,
        Func<TResult> onDeltecUk,
        Func<TResult> onDtdcExpress,
        Func<TResult> onTourline,
        Func<TResult> onBhWorldwide,
        Func<TResult> onOcs,
        Func<TResult> onYingnuoLogistics,
        Func<TResult> onUps,
        Func<TResult> onToll,
        Func<TResult> onPrtSeur,
        Func<TResult> onDtdcAu,
        Func<TResult> onThaDynamicLogistics,
        Func<TResult> onUbiLogistics,
        Func<TResult> onFedexCrossborder,
        Func<TResult> onA1Post,
        Func<TResult> onTazmanianFreight,
        Func<TResult> onCjIntMy,
        Func<TResult> onSaiaFreight,
        Func<TResult> onSgQxpress,
        Func<TResult> onNhansSolutions,
        Func<TResult> onDpdFr,
        Func<TResult> onCoordinadora,
        Func<TResult> onAndreani,
        Func<TResult> onDoora,
        Func<TResult> onInterparcelNz,
        Func<TResult> onPhlJamexpress,
        Func<TResult> onBelBelgiumPost,
        Func<TResult> onUsApc,
        Func<TResult> onIdnPos,
        Func<TResult> onFrMondial,
        Func<TResult> onDeDhl,
        Func<TResult> onHkRpx,
        Func<TResult> onDhlPieceid,
        Func<TResult> onVnpostEms,
        Func<TResult> onRrdonnelley,
        Func<TResult> onDpdDe,
        Func<TResult> onDelcartIn,
        Func<TResult> onImexglobalsolutions,
        Func<TResult> onAcommerce,
        Func<TResult> onEurodis,
        Func<TResult> onCanpar,
        Func<TResult> onGls,
        Func<TResult> onIndEcom,
        Func<TResult> onEspEnvialia,
        Func<TResult> onDhlUk,
        Func<TResult> onSmsaExpress,
        Func<TResult> onTntFr,
        Func<TResult> onDexI,
        Func<TResult> onBudbeeWebhook,
        Func<TResult> onCopaCourier,
        Func<TResult> onVnmVietnamPost,
        Func<TResult> onDpdHk,
        Func<TResult> onTollNz,
        Func<TResult> onEcho,
        Func<TResult> onFedexFr,
        Func<TResult> onBorderexpress,
        Func<TResult> onMailplusJpn,
        Func<TResult> onTntUkRefr,
        Func<TResult> onKec,
        Func<TResult> onDpdRo,
        Func<TResult> onTntJp,
        Func<TResult> onThCj,
        Func<TResult> onEcCn,
        Func<TResult> onFastwayUk,
        Func<TResult> onFastwayUs,
        Func<TResult> onGlsDe,
        Func<TResult> onGlsEs,
        Func<TResult> onGlsFr,
        Func<TResult> onMondialBe,
        Func<TResult> onSgtIt,
        Func<TResult> onTntCn,
        Func<TResult> onTntDe,
        Func<TResult> onTntEs,
        Func<TResult> onTntPl,
        Func<TResult> onParcelforce,
        Func<TResult> onSwissPost,
        Func<TResult> onTollIpec,
        Func<TResult> onAir21,
        Func<TResult> onAirspeed,
        Func<TResult> onBert,
        Func<TResult> onBluedart,
        Func<TResult> onCollectplus,
        Func<TResult> onCourierplus,
        Func<TResult> onCourierPost,
        Func<TResult> onDhlGlobalMail,
        Func<TResult> onDpdUk,
        Func<TResult> onDeltecDe,
        Func<TResult> onDeutscheDe,
        Func<TResult> onDotzot,
        Func<TResult> onEltaGr,
        Func<TResult> onEmsCn,
        Func<TResult> onEcargo,
        Func<TResult> onEnsenda,
        Func<TResult> onFercamIt,
        Func<TResult> onFastwayZa,
        Func<TResult> onFastwayAu,
        Func<TResult> onFirstLogisitcs,
        Func<TResult> onGeodis,
        Func<TResult> onGlobegistics,
        Func<TResult> onGreyhound,
        Func<TResult> onJetshipMy,
        Func<TResult> onLionParcel,
        Func<TResult> onAeroflash,
        Func<TResult> onOntrac,
        Func<TResult> onSagawa,
        Func<TResult> onSiodemka,
        Func<TResult> onStartrack,
        Func<TResult> onTntAu,
        Func<TResult> onTntIt,
        Func<TResult> onTransmission,
        Func<TResult> onYamato,
        Func<TResult> onDhlIt,
        Func<TResult> onDhlAt,
        Func<TResult> onLogisticsworldwideKr,
        Func<TResult> onGlsSpain,
        Func<TResult> onAmazonUkApi,
        Func<TResult> onDpdFrReference,
        Func<TResult> onDhlparcelUk,
        Func<TResult> onMegasave,
        Func<TResult> onQualitypost,
        Func<TResult> onIdsLogistics,
        Func<TResult> onJoyingbox,
        Func<TResult> onPantherOrderNumber,
        Func<TResult> onWatkinsShepard,
        Func<TResult> onFasttrack,
        Func<TResult> onUpExpress,
        Func<TResult> onElogistica,
        Func<TResult> onEcourier,
        Func<TResult> onCjPhilippines,
        Func<TResult> onSpeedex,
        Func<TResult> onOrangeconnex,
        Func<TResult> onTecor,
        Func<TResult> onSaee,
        Func<TResult> onGlsItalyFtp,
        Func<TResult> onDelivere,
        Func<TResult> onYycom,
        Func<TResult> onAdicionalPt,
        Func<TResult> onDksh,
        Func<TResult> onNipponExpressFtp,
        Func<TResult> onGols,
        Func<TResult> onFujexp,
        Func<TResult> onQtrack,
        Func<TResult> onOmlogisticsApi,
        Func<TResult> onGdpharm,
        Func<TResult> onMisumiCn,
        Func<TResult> onAirCanada,
        Func<TResult> onCity56Webhook,
        Func<TResult> onSagawaApi,
        Func<TResult> onKedaex,
        Func<TResult> onPgeonApi,
        Func<TResult> onWeworldexpress,
        Func<TResult> onJtLogistics,
        Func<TResult> onTrusk,
        Func<TResult> onViaxpress,
        Func<TResult> onDhlSupplychainId,
        Func<TResult> onZuelligpharmaSftp,
        Func<TResult> onMeest,
        Func<TResult> onTollPriority,
        Func<TResult> onMothershipApi,
        Func<TResult> onCapital,
        Func<TResult> onEuropaketApi,
        Func<TResult> onHfd,
        Func<TResult> onTourlineReference,
        Func<TResult> onGioEcourier,
        Func<TResult> onCnLogistics,
        Func<TResult> onPandion,
        Func<TResult> onBpostApi,
        Func<TResult> onPassportshipping,
        Func<TResult> onPakajo,
        Func<TResult> onDachser,
        Func<TResult> onYusenSftp,
        Func<TResult> onShyplite,
        Func<TResult> onXyy,
        Func<TResult> onMwd,
        Func<TResult> onFaxecargo,
        Func<TResult> onMazet,
        Func<TResult> onFirstLogisticsApi,
        Func<TResult> onSprintPack,
        Func<TResult> onHermesDeFtp,
        Func<TResult> onConcise,
        Func<TResult> onKerryExpressTwApi,
        Func<TResult> onEwe,
        Func<TResult> onFastdespatch,
        Func<TResult> onAbcustomSftp,
        Func<TResult> onChazki,
        Func<TResult> onShippie,
        Func<TResult> onGeodisApi,
        Func<TResult> onNaqelExpress,
        Func<TResult> onPapaWebhook,
        Func<TResult> onForwardair,
        Func<TResult> onDialogoLogisticaApi,
        Func<TResult> onLalamoveApi,
        Func<TResult> onTomydoor,
        Func<TResult> onKronosWebhook,
        Func<TResult> onJtcargo,
        Func<TResult> onTCat,
        Func<TResult> onConciseWebhook,
        Func<TResult> onTeleportWebhook,
        Func<TResult> onCustomcoApi,
        Func<TResult> onSpxTh,
        Func<TResult> onBolloreLogistics,
        Func<TResult> onClicklinkSftp,
        Func<TResult> onM3Logistics,
        Func<TResult> onVnpostApi,
        Func<TResult> onAxlehireFtp,
        Func<TResult> onShadowfax,
        Func<TResult> onMyhermesUkApi,
        Func<TResult> onDaiichi,
        Func<TResult> onMensajerosurbanosApi,
        Func<TResult> onPolarspeed,
        Func<TResult> onIdexpressId,
        Func<TResult> onPayo,
        Func<TResult> onWhistlSftp,
        Func<TResult> onIntexDe,
        Func<TResult> onTrans2U,
        Func<TResult> onProductcaregroupSftp,
        Func<TResult> onBigsmart,
        Func<TResult> onExpeditorsApiRef,
        Func<TResult> onAitworldwideApi,
        Func<TResult> onWorldcourier,
        Func<TResult> onQuiqup,
        Func<TResult> onAgedissSftp,
        Func<TResult> onAndreaniApi,
        Func<TResult> onCrlexpress,
        Func<TResult> onSmartcat,
        Func<TResult> onCrossflight,
        Func<TResult> onProcarrier,
        Func<TResult> onDhlReferenceApi,
        Func<TResult> onSeinoApi,
        Func<TResult> onWspexpress,
        Func<TResult> onKronos,
        Func<TResult> onTotalExpressApi,
        Func<TResult> onParcll,
        Func<TResult> onXpedigo,
        Func<TResult> onStarTrackWebhook,
        Func<TResult> onGpost,
        Func<TResult> onUcs,
        Func<TResult> onDmfgroup,
        Func<TResult> onCoordinadoraApi,
        Func<TResult> onMarken,
        Func<TResult> onNtl,
        Func<TResult> onRedjepakketje,
        Func<TResult> onAlliedExpressFtp,
        Func<TResult> onMondialrelayEs,
        Func<TResult> onNaekoFtp,
        Func<TResult> onMhi,
        Func<TResult> onShippify,
        Func<TResult> onMalcaAmitApi,
        Func<TResult> onJtexpressSgApi,
        Func<TResult> onDachserWeb,
        Func<TResult> onFlightlg,
        Func<TResult> onCago,
        Func<TResult> onCom1Express,
        Func<TResult> onTonamiFtp,
        Func<TResult> onPackfleet,
        Func<TResult> onPurolatorInternational,
        Func<TResult> onWineshippingWebhook,
        Func<TResult> onDhlEsSftp,
        Func<TResult> onPchomeApi,
        Func<TResult> onCeskapostaApi,
        Func<TResult> onGorush,
        Func<TResult> onHomerunner,
        Func<TResult> onAmazonOrder,
        Func<TResult> onEfwnowApi,
        Func<TResult> onCblLogisticaApi,
        Func<TResult> onNimbuspost,
        Func<TResult> onLogwinLogistics,
        Func<TResult> onNowlogApi,
        Func<TResult> onDpdNl,
        Func<TResult> onGodependable,
        Func<TResult> onEsdex,
        Func<TResult> onLogisystemsSftp,
        Func<TResult> onExpeditors,
        Func<TResult> onSntglobalApi,
        Func<TResult> onShipx,
        Func<TResult> onQintlApi,
        Func<TResult> onPacks,
        Func<TResult> onPostnlInternational,
        Func<TResult> onAmazonEmailPush,
        Func<TResult> onDhlApi,
        Func<TResult> onSpx,
        Func<TResult> onAxlehire,
        Func<TResult> onIcscourier,
        Func<TResult> onDialogoLogistica,
        Func<TResult> onShunbangExpress,
        Func<TResult> onTcsApi,
        Func<TResult> onSfExpressCn,
        Func<TResult> onPacketa,
        Func<TResult> onSicTeliway,
        Func<TResult> onMondialrelayFr,
        Func<TResult> onIntimeFtp,
        Func<TResult> onJdExpress,
        Func<TResult> onFastbox,
        Func<TResult> onPatheon,
        Func<TResult> onIndiaPost,
        Func<TResult> onTipsaRef,
        Func<TResult> onEcofreight,
        Func<TResult> onVox,
        Func<TResult> onDirectfreightAuRef,
        Func<TResult> onBesttransportSftp,
        Func<TResult> onAustraliaPostApi,
        Func<TResult> onFragilepakSftp,
        Func<TResult> onFlipxp,
        Func<TResult> onValueWebhook,
        Func<TResult> onDaeshin,
        Func<TResult> onSherpa,
        Func<TResult> onMwdApi,
        Func<TResult> onSmartkargo,
        Func<TResult> onDnjExpress,
        Func<TResult> onGopeople,
        Func<TResult> onMysendleApi,
        Func<TResult> onAramexApi,
        Func<TResult> onPidge,
        Func<TResult> onThaiparcels,
        Func<TResult> onPantherReferenceApi,
        Func<TResult> onPostaplus,
        Func<TResult> onBuffalo,
        Func<TResult> onUEnvios,
        Func<TResult> onEliteCo,
        Func<TResult> onRocheInternalSftp,
        Func<TResult> onDbschenkerIceland,
        Func<TResult> onTntFrReference,
        Func<TResult> onNewgisticsapi,
        Func<TResult> onGlovo,
        Func<TResult> onGwlogisApi,
        Func<TResult> onSpreetailApi,
        Func<TResult> onMoova,
        Func<TResult> onPlycongroup,
        Func<TResult> onUspsWebhook,
        Func<TResult> onReimaginedelivery,
        Func<TResult> onEdfFtp,
        Func<TResult> onDao365,
        Func<TResult> onBiocairFtp,
        Func<TResult> onRansaWebhook,
        Func<TResult> onShipxpres,
        Func<TResult> onCourantPlusApi,
        Func<TResult> onShipa,
        Func<TResult> onHomelogistics,
        Func<TResult> onDx,
        Func<TResult> onPosteItalianePaccocelere,
        Func<TResult> onTollWebhook,
        Func<TResult> onLctbrApi,
        Func<TResult> onDxFreight,
        Func<TResult> onDhlSftp,
        Func<TResult> onShiprocket,
        Func<TResult> onUberWebhook,
        Func<TResult> onStatovernight,
        Func<TResult> onBurd,
        Func<TResult> onFastship,
        Func<TResult> onIbventureWebhook,
        Func<TResult> onGatiKweApi,
        Func<TResult> onCryopdpFtp,
        Func<TResult> onHubbed,
        Func<TResult> onTipsaApi,
        Func<TResult> onAraskargo,
        Func<TResult> onThijsNl,
        Func<TResult> onAtshealthcareReference,
        Func<TResult> on_99Minutos,
        Func<TResult> onHellenicPost,
        Func<TResult> onHsmGlobal,
        Func<TResult> onMnx,
        Func<TResult> onNmtransfer,
        Func<TResult> onLogysto,
        Func<TResult> onIndiaPostInt,
        Func<TResult> onAmazonFbaSwishipIn,
        Func<TResult> onSrtTransport,
        Func<TResult> onBomi,
        Func<TResult> onDeliverrSftp,
        Func<TResult> onHsdexpress,
        Func<TResult> onSimpletireWebhook,
        Func<TResult> onHunterExpressSftp,
        Func<TResult> onUpsApi,
        Func<TResult> onWooyoungLogisticsSftp,
        Func<TResult> onPhseApi,
        Func<TResult> onWishEmailPush,
        Func<TResult> onNorthline,
        Func<TResult> onMedafrica,
        Func<TResult> onDpdAtSftp,
        Func<TResult> onAnteraja,
        Func<TResult> onDhlGlobalForwardingApi,
        Func<TResult> onLbcexpressApi,
        Func<TResult> onSimsglobal,
        Func<TResult> onCdldelivers,
        Func<TResult> onTyp,
        Func<TResult> onTestingCourierWebhook,
        Func<TResult> onPandagoApi,
        Func<TResult> onRoyalMailFtp,
        Func<TResult> onThunderexpress,
        Func<TResult> onSecretlabWebhook,
        Func<TResult> onSetel,
        Func<TResult> onJdWorldwide,
        Func<TResult> onDpdRuApi,
        Func<TResult> onArgentsWebhook,
        Func<TResult> onPostone,
        Func<TResult> onTusklogistics,
        Func<TResult> onRhenusUkApi,
        Func<TResult> onTaqbinSgApi,
        Func<TResult> onInntralogSftp,
        Func<TResult> onDayross,
        Func<TResult> onCorreosexpressApi,
        Func<TResult> onInternationalSeurApi,
        Func<TResult> onYodelApi,
        Func<TResult> onHeroexpress,
        Func<TResult> onDhlSupplychainIn,
        Func<TResult> onUrgentCargus,
        Func<TResult> onFrontdoorcorp,
        Func<TResult> onJtexpressPh,
        Func<TResult> onParcelstarsWebhook,
        Func<TResult> onDpdSkSftp,
        Func<TResult> onMovianto,
        Func<TResult> onOzepartsShipping,
        Func<TResult> onKargomkolay,
        Func<TResult> onTrunkrs,
        Func<TResult> onOmnirpsWebhook,
        Func<TResult> onChilexpress,
        Func<TResult> onTestingCourier,
        Func<TResult> onJneApi,
        Func<TResult> onBjshomedeliveryFtp,
        Func<TResult> onDexpressWebhook,
        Func<TResult> onUspsApi,
        Func<TResult> onTransvirtual,
        Func<TResult> onSolisticaApi,
        Func<TResult> onChienventureWebhook,
        Func<TResult> onDpdUkSftp,
        Func<TResult> onInpostUk,
        Func<TResult> onJavit,
        Func<TResult> onZtoDomestic,
        Func<TResult> onDhlGtApi,
        Func<TResult> onCevaTracking,
        Func<TResult> onKomonExpress,
        Func<TResult> onEastwestcourierFtp,
        Func<TResult> onDanniao,
        Func<TResult> onSpectran,
        Func<TResult> onDeliverIt,
        Func<TResult> onRelaiscolis,
        Func<TResult> onGlsSpainApi,
        Func<TResult> onPostplus,
        Func<TResult> onAirterra,
        Func<TResult> onGioEcourierApi,
        Func<TResult> onDpdChSftp,
        Func<TResult> onFedexApi,
        Func<TResult> onIntersmarttrans,
        Func<TResult> onHermesUkSftp,
        Func<TResult> onExelotFtp,
        Func<TResult> onDhlPaApi,
        Func<TResult> onVirtransportSftp,
        Func<TResult> onWorldnet,
        Func<TResult> onInstaboxWebhook,
        Func<TResult> onKng,
        Func<TResult> onFlashexpressWebhook,
        Func<TResult> onMagyarPostaApi,
        Func<TResult> onWeshipApi,
        Func<TResult> onOhiWebhook,
        Func<TResult> onMudita,
        Func<TResult> onBluedartApi,
        Func<TResult> onTCatApi,
        Func<TResult> onAds,
        Func<TResult> onHermesIt,
        Func<TResult> onFitzmarkApi,
        Func<TResult> onPostiApi,
        Func<TResult> onSmsaExpressWebhook,
        Func<TResult> onTamergroupWebhook,
        Func<TResult> onLivrapide,
        Func<TResult> onNipponExpress,
        Func<TResult> onBettertrucks,
        Func<TResult> onFan,
        Func<TResult> onPbUspsflatsFtp,
        Func<TResult> onParcelright,
        Func<TResult> onIthinklogistics,
        Func<TResult> onKerryExpressThWebhook,
        Func<TResult> onEcoutier,
        Func<TResult> onShowl,
        Func<TResult> onBrtItApi,
        Func<TResult> onRixonhkApi,
        Func<TResult> onDbschenkerApi,
        Func<TResult> onIlyanglogis,
        Func<TResult> onMailBoxEtc,
        Func<TResult> onWeship,
        Func<TResult> onDhlGlobalMailApi,
        Func<TResult> onActivos24Api,
        Func<TResult> onAtshealthcare,
        Func<TResult> onLuwjistik,
        Func<TResult> onGwWorld,
        Func<TResult> onFairsendenApi,
        Func<TResult> onServipWebhook,
        Func<TResult> onSwiship,
        Func<TResult> onTanet,
        Func<TResult> onHotsinCargo,
        Func<TResult> onDirex,
        Func<TResult> onHuantong,
        Func<TResult> onImileApi,
        Func<TResult> onAuexpress,
        Func<TResult> onNytlogistics,
        Func<TResult> onDsvReference,
        Func<TResult> onNovofarmaWebhook,
        Func<TResult> onAitworldwideSftp,
        Func<TResult> onShopolive,
        Func<TResult> onFnfZa,
        Func<TResult> onDhlEcommerceGc,
        Func<TResult> onFetchr,
        Func<TResult> onStarlinksApi,
        Func<TResult> onYyexpress,
        Func<TResult> onServientrega,
        Func<TResult> onHanjin,
        Func<TResult> onSpanishSeurFtp,
        Func<TResult> onDxB2BConnum,
        Func<TResult> onHelthjemApi,
        Func<TResult> onInexpost,
        Func<TResult> onA2BBa,
        Func<TResult> onRhenusGroup,
        Func<TResult> onSberlogisticsRu,
        Func<TResult> onMalcaAmit,
        Func<TResult> onPpl,
        Func<TResult> onOsmWorldwideSftp,
        Func<TResult> onAcilogistix,
        Func<TResult> onOptimacourier,
        Func<TResult> onNovaPoshtaApi,
        Func<TResult> onLoggi,
        Func<TResult> onYifan,
        Func<TResult> onMydynalogic,
        Func<TResult> onMorninglobal,
        Func<TResult> onConciseApi,
        Func<TResult> onFxtran,
        Func<TResult> onDeliveryourparcelZa,
        Func<TResult> onUparcel,
        Func<TResult> onMobiBr,
        Func<TResult> onLoginextWebhook,
        Func<TResult> onEms,
        Func<TResult> onSpeedy,
        Func<TResult> onZoomRed,
        Func<TResult> onNavlungo,
        Func<TResult> onCastleparcels,
        Func<TResult> onWeee,
        Func<TResult> onPackaly,
        Func<TResult> onYunhuipost,
        Func<TResult> onYouparcel,
        Func<TResult> onLeman,
        Func<TResult> onMoovin,
        Func<TResult> onUrbIt,
        Func<TResult> onMultientregapanama,
        Func<TResult> onJusdasr,
        Func<TResult> onDiscountpost,
        Func<TResult> onRhenusUk,
        Func<TResult> onSwishipJp,
        Func<TResult> onGlsUs,
        Func<TResult> onSmtl,
        Func<TResult> onEmega,
        Func<TResult> onExpressoneSv,
        Func<TResult> onHepsijet,
        Func<TResult> onWelivery,
        Func<TResult> onBringer,
        Func<TResult> onEasyroutes,
        Func<TResult> onMrw,
        Func<TResult> onRpm,
        Func<TResult> onDpdPrt,
        Func<TResult> onGlsRomania,
        Func<TResult> onLmparcel,
        Func<TResult> onGtagsm,
        Func<TResult> onDomino,
        Func<TResult> onEshipper,
        Func<TResult> onTranspak,
        Func<TResult> onXindus,
        Func<TResult> onAoyue,
        Func<TResult> onEasyparcel,
        Func<TResult> onExpressone,
        Func<TResult> onSendeoKargo,
        Func<TResult> onSpeedaf,
        Func<TResult> onEtower,
        Func<TResult> onGcx,
        Func<TResult> onNinjavanVn,
        Func<TResult> onAllegro,
        Func<TResult> onJumppoint,
        Func<TResult> onShipglobalUs,
        Func<TResult> onKinisi,
        Func<TResult> onOakh,
        Func<TResult> onAwest,
        Func<TResult> onBarsan,
        Func<TResult> onEnergologistic,
        Func<TResult> onMadrooex,
        Func<TResult> onGobolt,
        Func<TResult> onSwissUniversalExpress,
        Func<TResult> onIordirect,
        Func<TResult> onXmszm,
        Func<TResult> onGlsHun,
        Func<TResult> onSendy,
        Func<TResult> onBraunsexpress,
        Func<TResult> onGrandslamexpress,
        Func<TResult> onXgs,
        Func<TResult> onOtschile,
        Func<TResult> onPackUp,
        Func<TResult> onParcelstars,
        Func<TResult> onTeamexpressllc,
        Func<TResult> onAsyadexpress,
        Func<TResult> onTdn,
        Func<TResult> onEarlybird,
        Func<TResult> onCacesa,
        Func<TResult> onParceljet,
        Func<TResult> onMngKargo,
        Func<TResult> onSuperpackline,
        Func<TResult> onSpeedx,
        Func<TResult> onVesyl,
        Func<TResult> onSkyking,
        Func<TResult> onDirmensajeria,
        Func<TResult> onNetlogixgroup,
        Func<TResult> onZyou,
        Func<TResult> onJawar,
        Func<TResult> onAgsystems,
        Func<TResult> onGps,
        Func<TResult> onPttKargo,
        Func<TResult> onMaergo,
        Func<TResult> onArihantcourier,
        Func<TResult> onVtfe,
        Func<TResult> onYunant,
        Func<TResult> onUrbify,
        Func<TResult> onPackMan,
        Func<TResult> onLiefergrun,
        Func<TResult> onObibox,
        Func<TResult> onPaikeda,
        Func<TResult> onScotty,
        Func<TResult> onIntelcomCa,
        Func<TResult> onSwe,
        Func<TResult> onAsendia,
        Func<TResult> onDpdAt,
        Func<TResult> onRelay,
        Func<TResult> onAta,
        Func<TResult> onSkyexpressInternational,
        Func<TResult> onSuratKargo,
        Func<TResult> onSglink,
        Func<TResult> onFleetopticsinc,
        Func<TResult> onShopline,
        Func<TResult> onPiggyship,
        Func<TResult> onLogoix,
        Func<TResult> onKolayGelsin,
        Func<TResult> onAssociatedCouriers,
        Func<TResult> onUpsChecker,
        Func<TResult> onWineshipping,
        Func<TResult> onSpedisci,
        Func<TResult> onFourkites,
        Func<TResult> onEtonas,
        Func<TResult> onFinmile,
        Func<TResult> onUniuni,
        Func<TResult> onRodonaves,
        Func<TResult> onInpostIt,
        Func<TResult> onTforceFreight,
        Func<TResult> onRichmom,
        Func<TResult> onFranco,
        Func<TResult> onEcparcel,
        Func<TResult> onFedexChina,
        Func<TResult> onGofoExpress,
        Func<TResult> onShipbob,
        Func<TResult> onJerseypostAtlas,
        Func<TResult> onCoretrails,
        Func<TResult> onRhenusItaly,
        Func<TResult> onJadlog,
        Func<TResult> onJitsu,
        Func<TResult> onYanwenExpress,
        Func<TResult> onDashlink,
        Func<TResult> onSeinoSuperExpress,
        Func<TResult> onFloship,
        Func<TResult> onMetroscg,
        Func<TResult> onSendparcel,
        Func<TResult> onP2P,
        Func<TResult> onCnExpress,
        Func<TResult> onCirrotrack,
        Func<TResult> onLandLogistics,
        Func<TResult> onVeho,
        Func<TResult> onMedline,
        Func<TResult> onVdtrack,
        Func<TResult> onSinoScm,
        Func<TResult> on_3PeExpress,
        Func<TResult> onSwiftx,
        Func<TResult> onSfydexpress,
        Func<TResult> onToptrans,
        Func<TResult> onOther,
        Func<string, TResult> otherwise) =>
        this switch
        {
            _ when this == DpdRu => onDpdRu(),
            _ when this == BgBulgarianPost => onBgBulgarianPost(),
            _ when this == KrKoreaPost => onKrKoreaPost(),
            _ when this == ZaCourierit => onZaCourierit(),
            _ when this == FrExapaq => onFrExapaq(),
            _ when this == AreEmiratesPost => onAreEmiratesPost(),
            _ when this == Gac => onGac(),
            _ when this == Geis => onGeis(),
            _ when this == SfEx => onSfEx(),
            _ when this == Pago => onPago(),
            _ when this == Myhermes => onMyhermes(),
            _ when this == DiamondEurogistics => onDiamondEurogistics(),
            _ when this == CorporatecouriersWebhook => onCorporatecouriersWebhook(),
            _ when this == Bond => onBond(),
            _ when this == Omniparcel => onOmniparcel(),
            _ when this == SkPosta => onSkPosta(),
            _ when this == Purolator => onPurolator(),
            _ when this == FetchrWebhook => onFetchrWebhook(),
            _ when this == Thedeliverygroup => onThedeliverygroup(),
            _ when this == CelloSquare => onCelloSquare(),
            _ when this == Tarrive => onTarrive(),
            _ when this == Collivery => onCollivery(),
            _ when this == Mainfreight => onMainfreight(),
            _ when this == IndFirstflight => onIndFirstflight(),
            _ when this == Acsworldwide => onAcsworldwide(),
            _ when this == Amstan => onAmstan(),
            _ when this == Okayparcel => onOkayparcel(),
            _ when this == EnvialiaReference => onEnvialiaReference(),
            _ when this == SeurEs => onSeurEs(),
            _ when this == Continental => onContinental(),
            _ when this == Fdsexpress => onFdsexpress(),
            _ when this == AmazonFbaSwiship => onAmazonFbaSwiship(),
            _ when this == Wyngs => onWyngs(),
            _ when this == DhlActiveTracing => onDhlActiveTracing(),
            _ when this == Zyllem => onZyllem(),
            _ when this == Ruston => onRuston(),
            _ when this == Xpost => onXpost(),
            _ when this == CorreosEs => onCorreosEs(),
            _ when this == DhlFr => onDhlFr(),
            _ when this == PanAsia => onPanAsia(),
            _ when this == BrtIt => onBrtIt(),
            _ when this == SreKorea => onSreKorea(),
            _ when this == Speedee => onSpeedee(),
            _ when this == TntUk => onTntUk(),
            _ when this == Venipak => onVenipak(),
            _ when this == Shreenandancourier => onShreenandancourier(),
            _ when this == Croshot => onCroshot(),
            _ when this == NipostNg => onNipostNg(),
            _ when this == EpstGlbl => onEpstGlbl(),
            _ when this == Newgistics => onNewgistics(),
            _ when this == PostSlovenia => onPostSlovenia(),
            _ when this == JerseyPost => onJerseyPost(),
            _ when this == Bombinoexp => onBombinoexp(),
            _ when this == Wmg => onWmg(),
            _ when this == XqExpress => onXqExpress(),
            _ when this == Furdeco => onFurdeco(),
            _ when this == LhtExpress => onLhtExpress(),
            _ when this == SouthAfricanPostOffice => onSouthAfricanPostOffice(),
            _ when this == Spoton => onSpoton(),
            _ when this == Dimerco => onDimerco(),
            _ when this == CyprusPostCyp => onCyprusPostCyp(),
            _ when this == Abcustom => onAbcustom(),
            _ when this == IndDelivree => onIndDelivree(),
            _ when this == CnBestexpress => onCnBestexpress(),
            _ when this == DxSftp => onDxSftp(),
            _ when this == PickuppMys => onPickuppMys(),
            _ when this == Fmx => onFmx(),
            _ when this == Hellmann => onHellmann(),
            _ when this == ShipItAsia => onShipItAsia(),
            _ when this == KerryEcommerce => onKerryEcommerce(),
            _ when this == Freterapido => onFreterapido(),
            _ when this == PitneyBowes => onPitneyBowes(),
            _ when this == XpressenDk => onXpressenDk(),
            _ when this == SeurSpApi => onSeurSpApi(),
            _ when this == Deliveryontime => onDeliveryontime(),
            _ when this == Jinsung => onJinsung(),
            _ when this == TransKargo => onTransKargo(),
            _ when this == SwishipDe => onSwishipDe(),
            _ when this == IvoyWebhook => onIvoyWebhook(),
            _ when this == AirmeeWebhook => onAirmeeWebhook(),
            _ when this == DhlBenelux => onDhlBenelux(),
            _ when this == Firstmile => onFirstmile(),
            _ when this == FastwayIr => onFastwayIr(),
            _ when this == HhExp => onHhExp(),
            _ when this == MysMypostOnline => onMysMypostOnline(),
            _ when this == TntNl => onTntNl(),
            _ when this == Tipsa => onTipsa(),
            _ when this == TaqbinMy => onTaqbinMy(),
            _ when this == Kgmhub => onKgmhub(),
            _ when this == Intexpress => onIntexpress(),
            _ when this == OverseExp => onOverseExp(),
            _ when this == Oneclick => onOneclick(),
            _ when this == RoadrunnerFreight => onRoadrunnerFreight(),
            _ when this == GlsCrotia => onGlsCrotia(),
            _ when this == MrwFtp => onMrwFtp(),
            _ when this == Bluex => onBluex(),
            _ when this == Dylt => onDylt(),
            _ when this == DpdIr => onDpdIr(),
            _ when this == SinGlbl => onSinGlbl(),
            _ when this == TuffnellsReference => onTuffnellsReference(),
            _ when this == Cjpacket => onCjpacket(),
            _ when this == Milkman => onMilkman(),
            _ when this == Asigna => onAsigna(),
            _ when this == Oneworldexpress => onOneworldexpress(),
            _ when this == RoyalMail => onRoyalMail(),
            _ when this == ViaExpress => onViaExpress(),
            _ when this == Tigfreight => onTigfreight(),
            _ when this == ZtoExpress => onZtoExpress(),
            _ when this == TwoGo => onTwoGo(),
            _ when this == Iml => onIml(),
            _ when this == IntelValley => onIntelValley(),
            _ when this == Efs => onEfs(),
            _ when this == UkUkMail => onUkUkMail(),
            _ when this == Ram => onRam(),
            _ when this == Alliedexpress => onAlliedexpress(),
            _ when this == ApcOvernight => onApcOvernight(),
            _ when this == Shippit => onShippit(),
            _ when this == Tfm => onTfm(),
            _ when this == MXpress => onMXpress(),
            _ when this == HdbBox => onHdbBox(),
            _ when this == ClevyLinks => onClevyLinks(),
            _ when this == Ibeone => onIbeone(),
            _ when this == FiegeNl => onFiegeNl(),
            _ when this == KweGlobal => onKweGlobal(),
            _ when this == CtcExpress => onCtcExpress(),
            _ when this == Amazon => onAmazon(),
            _ when this == MoreLink => onMoreLink(),
            _ when this == Jx => onJx(),
            _ when this == EasyMail => onEasyMail(),
            _ when this == Aduiepyle => onAduiepyle(),
            _ when this == GbPanther => onGbPanther(),
            _ when this == Expresssale => onExpresssale(),
            _ when this == SgDetrack => onSgDetrack(),
            _ when this == TrunkrsWebhook => onTrunkrsWebhook(),
            _ when this == Matdespatch => onMatdespatch(),
            _ when this == Dicom => onDicom(),
            _ when this == Mbw => onMbw(),
            _ when this == KhmCambodiaPost => onKhmCambodiaPost(),
            _ when this == Sinotrans => onSinotrans(),
            _ when this == BrtItParcelid => onBrtItParcelid(),
            _ when this == DhlSupplyChain => onDhlSupplyChain(),
            _ when this == DhlPl => onDhlPl(),
            _ when this == Topyou => onTopyou(),
            _ when this == Palexpress => onPalexpress(),
            _ when this == DhlSg => onDhlSg(),
            _ when this == CnWedo => onCnWedo(),
            _ when this == Fulfillme => onFulfillme(),
            _ when this == DpdDelistrack => onDpdDelistrack(),
            _ when this == UpsReference => onUpsReference(),
            _ when this == Caribou => onCaribou(),
            _ when this == LocusWebhook => onLocusWebhook(),
            _ when this == Dsv => onDsv(),
            _ when this == P2PTrc => onP2PTrc(),
            _ when this == Directparcels => onDirectparcels(),
            _ when this == NovaPoshtaInt => onNovaPoshtaInt(),
            _ when this == FedexPoland => onFedexPoland(),
            _ when this == CnJcex => onCnJcex(),
            _ when this == FarInternational => onFarInternational(),
            _ when this == Idexpress => onIdexpress(),
            _ when this == Gangbao => onGangbao(),
            _ when this == Neway => onNeway(),
            _ when this == PostnlInt3S => onPostnlInt3S(),
            _ when this == RpxId => onRpxId(),
            _ when this == DesignertransportWebhook => onDesignertransportWebhook(),
            _ when this == GlsSloven => onGlsSloven(),
            _ when this == ParcelledIn => onParcelledIn(),
            _ when this == GsiExpress => onGsiExpress(),
            _ when this == ConWay => onConWay(),
            _ when this == BrouwerTransport => onBrouwerTransport(),
            _ when this == Cpex => onCpex(),
            _ when this == IsraelPost => onIsraelPost(),
            _ when this == DtdcIn => onDtdcIn(),
            _ when this == PttPost => onPttPost(),
            _ when this == XdeWebhook => onXdeWebhook(),
            _ when this == Tolos => onTolos(),
            _ when this == GiaoHang => onGiaoHang(),
            _ when this == GeodisEspace => onGeodisEspace(),
            _ when this == MagyarHu => onMagyarHu(),
            _ when this == DoordashWebhook => onDoordashWebhook(),
            _ when this == TikiId => onTikiId(),
            _ when this == CjHkInternational => onCjHkInternational(),
            _ when this == StarTrackExpress => onStarTrackExpress(),
            _ when this == Helthjem => onHelthjem(),
            _ when this == Sfb2C => onSfb2C(),
            _ when this == Freightquote => onFreightquote(),
            _ when this == LandmarkGlobalReference => onLandmarkGlobalReference(),
            _ when this == Parcel2Go => onParcel2Go(),
            _ when this == Delnext => onDelnext(),
            _ when this == Rcl => onRcl(),
            _ when this == CgsExpress => onCgsExpress(),
            _ when this == HkPost => onHkPost(),
            _ when this == SapExpress => onSapExpress(),
            _ when this == ParcelpostSg => onParcelpostSg(),
            _ when this == Hermes => onHermes(),
            _ when this == IndSafeexpress => onIndSafeexpress(),
            _ when this == Tophatterexpress => onTophatterexpress(),
            _ when this == Mglobal => onMglobal(),
            _ when this == Averitt => onAveritt(),
            _ when this == Leader => onLeader(),
            _ when this == _2Ebox => on_2Ebox(),
            _ when this == SgSpeedpost => onSgSpeedpost(),
            _ when this == DbschenkerSe => onDbschenkerSe(),
            _ when this == IsrPostDomestic => onIsrPostDomestic(),
            _ when this == Bestwayparcel => onBestwayparcel(),
            _ when this == AsendiaDe => onAsendiaDe(),
            _ when this == NightlineUk => onNightlineUk(),
            _ when this == TaqbinSg => onTaqbinSg(),
            _ when this == TckExpress => onTckExpress(),
            _ when this == EndeavourDelivery => onEndeavourDelivery(),
            _ when this == Nanjingwoyuan => onNanjingwoyuan(),
            _ when this == HeppnerFr => onHeppnerFr(),
            _ when this == EmpsCn => onEmpsCn(),
            _ when this == Fonsen => onFonsen(),
            _ when this == Pickrr => onPickrr(),
            _ when this == ApcOvernightConnum => onApcOvernightConnum(),
            _ when this == StarTrackNextFlight => onStarTrackNextFlight(),
            _ when this == Dajin => onDajin(),
            _ when this == UpsFreight => onUpsFreight(),
            _ when this == PostaPlus => onPostaPlus(),
            _ when this == Ceva => onCeva(),
            _ when this == Anserx => onAnserx(),
            _ when this == JsExpress => onJsExpress(),
            _ when this == Padtf => onPadtf(),
            _ when this == UpsMailInnovations => onUpsMailInnovations(),
            _ when this == Sypost => onSypost(),
            _ when this == AmazonShipMcf => onAmazonShipMcf(),
            _ when this == Yusen => onYusen(),
            _ when this == Bring => onBring(),
            _ when this == SdaIt => onSdaIt(),
            _ when this == Gba => onGba(),
            _ when this == Neweggexpress => onNeweggexpress(),
            _ when this == SpeedcouriersGr => onSpeedcouriersGr(),
            _ when this == Forrun => onForrun(),
            _ when this == Pickup => onPickup(),
            _ when this == Ecms => onEcms(),
            _ when this == Intelipost => onIntelipost(),
            _ when this == Flashexpress => onFlashexpress(),
            _ when this == CnSto => onCnSto(),
            _ when this == SekoSftp => onSekoSftp(),
            _ when this == HomeDeliverySolutions => onHomeDeliverySolutions(),
            _ when this == DpdHgry => onDpdHgry(),
            _ when this == KerryttcVn => onKerryttcVn(),
            _ when this == JoyingBox => onJoyingBox(),
            _ when this == TotalExpress => onTotalExpress(),
            _ when this == ZjsExpress => onZjsExpress(),
            _ when this == Starken => onStarken(),
            _ when this == Demandship => onDemandship(),
            _ when this == CnDpex => onCnDpex(),
            _ when this == AupostCn => onAupostCn(),
            _ when this == Logisters => onLogisters(),
            _ when this == Goglobalpost => onGoglobalpost(),
            _ when this == GlsCz => onGlsCz(),
            _ when this == PaackWebhook => onPaackWebhook(),
            _ when this == GrabWebhook => onGrabWebhook(),
            _ when this == Parcelpoint => onParcelpoint(),
            _ when this == Icumulus => onIcumulus(),
            _ when this == Daiglobaltrack => onDaiglobaltrack(),
            _ when this == GlobalIparcel => onGlobalIparcel(),
            _ when this == YurticiKargo => onYurticiKargo(),
            _ when this == CnPaypalPackage => onCnPaypalPackage(),
            _ when this == Parcel2Post => onParcel2Post(),
            _ when this == GlsIt => onGlsIt(),
            _ when this == PilLogistics => onPilLogistics(),
            _ when this == Heppner => onHeppner(),
            _ when this == GeneralOvernight => onGeneralOvernight(),
            _ when this == Happy2Point => onHappy2Point(),
            _ when this == Chitchats => onChitchats(),
            _ when this == Smooth => onSmooth(),
            _ when this == CleLogistics => onCleLogistics(),
            _ when this == Fiege => onFiege(),
            _ when this == MxCargo => onMxCargo(),
            _ when this == Ziingfinalmile => onZiingfinalmile(),
            _ when this == DaytonFreight => onDaytonFreight(),
            _ when this == Tcs => onTcs(),
            _ when this == Aex => onAex(),
            _ when this == HermesDe => onHermesDe(),
            _ when this == RoutificWebhook => onRoutificWebhook(),
            _ when this == Globavend => onGlobavend(),
            _ when this == CjLogistics => onCjLogistics(),
            _ when this == PalletNetwork => onPalletNetwork(),
            _ when this == RafPh => onRafPh(),
            _ when this == UkXdp => onUkXdp(),
            _ when this == PaperExpress => onPaperExpress(),
            _ when this == LaPosteSuivi => onLaPosteSuivi(),
            _ when this == Paquetexpress => onPaquetexpress(),
            _ when this == Liefery => onLiefery(),
            _ when this == StreckTransport => onStreckTransport(),
            _ when this == PonyExpress => onPonyExpress(),
            _ when this == AlwaysExpress => onAlwaysExpress(),
            _ when this == GbsBroker => onGbsBroker(),
            _ when this == CitylinkMy => onCitylinkMy(),
            _ when this == Alljoy => onAlljoy(),
            _ when this == Yodel => onYodel(),
            _ when this == YodelDir => onYodelDir(),
            _ when this == Stone3Pl => onStone3Pl(),
            _ when this == ParcelpalWebhook => onParcelpalWebhook(),
            _ when this == DhlEcomerceAsa => onDhlEcomerceAsa(),
            _ when this == Simplypost => onSimplypost(),
            _ when this == KyExpress => onKyExpress(),
            _ when this == Shenzhen => onShenzhen(),
            _ when this == UsLasership => onUsLasership(),
            _ when this == UcExpre => onUcExpre(),
            _ when this == Didadi => onDidadi(),
            _ when this == CjKr => onCjKr(),
            _ when this == DbschenkerB2B => onDbschenkerB2B(),
            _ when this == Mxe => onMxe(),
            _ when this == CaeDelivers => onCaeDelivers(),
            _ when this == Pfcexpress => onPfcexpress(),
            _ when this == Whistl => onWhistl(),
            _ when this == Wepost => onWepost(),
            _ when this == DhlParcelEs => onDhlParcelEs(),
            _ when this == Ddexpress => onDdexpress(),
            _ when this == AramexAu => onAramexAu(),
            _ when this == Bneed => onBneed(),
            _ when this == HkTgx => onHkTgx(),
            _ when this == LatvijasPasts => onLatvijasPasts(),
            _ when this == Viaeurope => onViaeurope(),
            _ when this == CorreoUy => onCorreoUy(),
            _ when this == ChronopostFr => onChronopostFr(),
            _ when this == JNet => onJNet(),
            _ when this == _6Ls => on_6Ls(),
            _ when this == BlrBelpost => onBlrBelpost(),
            _ when this == Birdsystem => onBirdsystem(),
            _ when this == Dobropost => onDobropost(),
            _ when this == WahanaId => onWahanaId(),
            _ when this == Weaship => onWeaship(),
            _ when this == Sonictl => onSonictl(),
            _ when this == Kwt => onKwt(),
            _ when this == AfllogFtp => onAfllogFtp(),
            _ when this == SkynetWorldwide => onSkynetWorldwide(),
            _ when this == NovaPoshta => onNovaPoshta(),
            _ when this == Seino => onSeino(),
            _ when this == Szendex => onSzendex(),
            _ when this == BpostInt => onBpostInt(),
            _ when this == DbschenkerSv => onDbschenkerSv(),
            _ when this == AoDeutschland => onAoDeutschland(),
            _ when this == EuFleetSolutions => onEuFleetSolutions(),
            _ when this == Pcfcorp => onPcfcorp(),
            _ when this == Linkbridge => onLinkbridge(),
            _ when this == Primamulticipta => onPrimamulticipta(),
            _ when this == Courex => onCourex(),
            _ when this == ZajilExpress => onZajilExpress(),
            _ when this == Collectco => onCollectco(),
            _ when this == Jtexpress => onJtexpress(),
            _ when this == FedexUk => onFedexUk(),
            _ when this == Uship => onUship(),
            _ when this == Pixsell => onPixsell(),
            _ when this == Shiptor => onShiptor(),
            _ when this == Cdek => onCdek(),
            _ when this == VnmViettelpost => onVnmViettelpost(),
            _ when this == CjCentury => onCjCentury(),
            _ when this == Gso => onGso(),
            _ when this == Viwo => onViwo(),
            _ when this == Skybox => onSkybox(),
            _ when this == Kerrytj => onKerrytj(),
            _ when this == NtlogisticsVn => onNtlogisticsVn(),
            _ when this == SdhScm => onSdhScm(),
            _ when this == Zinc => onZinc(),
            _ when this == DpeSouthAfrc => onDpeSouthAfrc(),
            _ when this == CeskaCz => onCeskaCz(),
            _ when this == AcsGr => onAcsGr(),
            _ when this == Dealersend => onDealersend(),
            _ when this == Jocom => onJocom(),
            _ when this == Cse => onCse(),
            _ when this == TforceFinalmile => onTforceFinalmile(),
            _ when this == ShipGate => onShipGate(),
            _ when this == Shipter => onShipter(),
            _ when this == NationalSameday => onNationalSameday(),
            _ when this == Yunexpress => onYunexpress(),
            _ when this == Cainiao => onCainiao(),
            _ when this == DmsMatrix => onDmsMatrix(),
            _ when this == Directlog => onDirectlog(),
            _ when this == AsendiaUs => onAsendiaUs(),
            _ when this == _3Jmslogistics => on_3Jmslogistics(),
            _ when this == LiccardiExpress => onLiccardiExpress(),
            _ when this == SkyPostal => onSkyPostal(),
            _ when this == Cnwangtong => onCnwangtong(),
            _ when this == PostnordLogisticsDk => onPostnordLogisticsDk(),
            _ when this == Logistika => onLogistika(),
            _ when this == Celeritas => onCeleritas(),
            _ when this == Pressiode => onPressiode(),
            _ when this == ShreeMaruti => onShreeMaruti(),
            _ when this == LogisticsworldwideHk => onLogisticsworldwideHk(),
            _ when this == Efex => onEfex(),
            _ when this == Lotte => onLotte(),
            _ when this == Lonestar => onLonestar(),
            _ when this == Aprisaexpress => onAprisaexpress(),
            _ when this == BelRs => onBelRs(),
            _ when this == OsmWorldwide => onOsmWorldwide(),
            _ when this == WestgateGl => onWestgateGl(),
            _ when this == Fastrack => onFastrack(),
            _ when this == DtdExpr => onDtdExpr(),
            _ when this == Alfatrex => onAlfatrex(),
            _ when this == Promeddelivery => onPromeddelivery(),
            _ when this == ThabitLogistics => onThabitLogistics(),
            _ when this == HctLogistics => onHctLogistics(),
            _ when this == CarryFlap => onCarryFlap(),
            _ when this == UsOldDominion => onUsOldDominion(),
            _ when this == AnicamBox => onAnicamBox(),
            _ when this == Wanbexpress => onWanbexpress(),
            _ when this == AnPost => onAnPost(),
            _ when this == DpdLocal => onDpdLocal(),
            _ when this == Stallionexpress => onStallionexpress(),
            _ when this == Raiderex => onRaiderex(),
            _ when this == Shopfans => onShopfans(),
            _ when this == KyungdongParcel => onKyungdongParcel(),
            _ when this == ChampionLogistics => onChampionLogistics(),
            _ when this == PickuppSgp => onPickuppSgp(),
            _ when this == MorningExpress => onMorningExpress(),
            _ when this == Nacex => onNacex(),
            _ when this == ThenileWebhook => onThenileWebhook(),
            _ when this == Holisol => onHolisol(),
            _ when this == LbcexpressFtp => onLbcexpressFtp(),
            _ when this == Kurasi => onKurasi(),
            _ when this == UsfReddaway => onUsfReddaway(),
            _ when this == Apg => onApg(),
            _ when this == CnBoxc => onCnBoxc(),
            _ when this == Ecoscooting => onEcoscooting(),
            _ when this == Mainway => onMainway(),
            _ when this == Paperfly => onPaperfly(),
            _ when this == Houndexpress => onHoundexpress(),
            _ when this == BoxBerry => onBoxBerry(),
            _ when this == EpBox => onEpBox(),
            _ when this == PlusLogUk => onPlusLogUk(),
            _ when this == Fulfilla => onFulfilla(),
            _ when this == Ase => onAse(),
            _ when this == MailPlus => onMailPlus(),
            _ when this == XpoLogistics => onXpoLogistics(),
            _ when this == Wndirect => onWndirect(),
            _ when this == CloudwishAsia => onCloudwishAsia(),
            _ when this == Zeleris => onZeleris(),
            _ when this == GioExpress => onGioExpress(),
            _ when this == OcsWorldwide => onOcsWorldwide(),
            _ when this == ArkLogistics => onArkLogistics(),
            _ when this == Aquiline => onAquiline(),
            _ when this == PilotFreight => onPilotFreight(),
            _ when this == Qwintry => onQwintry(),
            _ when this == DanskeFragt => onDanskeFragt(),
            _ when this == Carriers => onCarriers(),
            _ when this == AirCanadaGlobal => onAirCanadaGlobal(),
            _ when this == PresidentTrans => onPresidentTrans(),
            _ when this == Stepforwardfs => onStepforwardfs(),
            _ when this == SkynetUk => onSkynetUk(),
            _ when this == Pittohio => onPittohio(),
            _ when this == CorreosExpress => onCorreosExpress(),
            _ when this == RlUs => onRlUs(),
            _ when this == Destiny => onDestiny(),
            _ when this == UkYodel => onUkYodel(),
            _ when this == CometTech => onCometTech(),
            _ when this == DhlParcelRu => onDhlParcelRu(),
            _ when this == TntRefr => onTntRefr(),
            _ when this == ShreeAnjaniCourier => onShreeAnjaniCourier(),
            _ when this == MikropakketBe => onMikropakketBe(),
            _ when this == EtsExpress => onEtsExpress(),
            _ when this == ColisPrive => onColisPrive(),
            _ when this == CnYunda => onCnYunda(),
            _ when this == AaaCooper => onAaaCooper(),
            _ when this == RocketParcel => onRocketParcel(),
            _ when this == _360Lion => on_360Lion(),
            _ when this == Pandu => onPandu(),
            _ when this == ProfessionalCouriers => onProfessionalCouriers(),
            _ when this == Flytexpress => onFlytexpress(),
            _ when this == LogisticsworldwideMy => onLogisticsworldwideMy(),
            _ when this == CorreosDeEspana => onCorreosDeEspana(),
            _ when this == Imx => onImx(),
            _ when this == FourPxExpress => onFourPxExpress(),
            _ when this == Xpressbees => onXpressbees(),
            _ when this == PickuppVnm => onPickuppVnm(),
            _ when this == StartrackExpress => onStartrackExpress(),
            _ when this == FrColissimo => onFrColissimo(),
            _ when this == NacexSpainReference => onNacexSpainReference(),
            _ when this == DhlSupplyChainAu => onDhlSupplyChainAu(),
            _ when this == Eshipping => onEshipping(),
            _ when this == Shreetirupati => onShreetirupati(),
            _ when this == HxExpress => onHxExpress(),
            _ when this == Indopaket => onIndopaket(),
            _ when this == Cn17Post => onCn17Post(),
            _ when this == K1Express => onK1Express(),
            _ when this == CjGls => onCjGls(),
            _ when this == MysGdex => onMysGdex(),
            _ when this == Nationex => onNationex(),
            _ when this == Anjun => onAnjun(),
            _ when this == Fargood => onFargood(),
            _ when this == SmgExpress => onSmgExpress(),
            _ when this == Rzyexpress => onRzyexpress(),
            _ when this == Sefl => onSefl(),
            _ when this == TntClickIt => onTntClickIt(),
            _ when this == Hdb => onHdb(),
            _ when this == Hipshipper => onHipshipper(),
            _ when this == Rpxlogistics => onRpxlogistics(),
            _ when this == Kuehne => onKuehne(),
            _ when this == ItNexive => onItNexive(),
            _ when this == Pts => onPts(),
            _ when this == SwissPostFtp => onSwissPostFtp(),
            _ when this == FastrkServ => onFastrkServ(),
            _ when this == _472 => on_472(),
            _ when this == UsYrc => onUsYrc(),
            _ when this == PostnlIntl3S => onPostnlIntl3S(),
            _ when this == ElianPost => onElianPost(),
            _ when this == Cubyn => onCubyn(),
            _ when this == SauSaudiPost => onSauSaudiPost(),
            _ when this == AbxexpressMy => onAbxexpressMy(),
            _ when this == HuahanExpress => onHuahanExpress(),
            _ when this == ZesExpress => onZesExpress(),
            _ when this == ZeptoExpress => onZeptoExpress(),
            _ when this == SkynetZa => onSkynetZa(),
            _ when this == Zeek2Door => onZeek2Door(),
            _ when this == Blinklastmile => onBlinklastmile(),
            _ when this == PostaUkr => onPostaUkr(),
            _ when this == Chrobinson => onChrobinson(),
            _ when this == CnPost56 => onCnPost56(),
            _ when this == CourantPlus => onCourantPlus(),
            _ when this == ScudexExpress => onScudexExpress(),
            _ when this == Shipentegra => onShipentegra(),
            _ when this == BTwoCEurope => onBTwoCEurope(),
            _ when this == Cope => onCope(),
            _ when this == IndGati => onIndGati(),
            _ when this == CnWishpost => onCnWishpost(),
            _ when this == NacexEs => onNacexEs(),
            _ when this == TaqbinHk => onTaqbinHk(),
            _ when this == Globaltranz => onGlobaltranz(),
            _ when this == Hkd => onHkd(),
            _ when this == Bjshomedelivery => onBjshomedelivery(),
            _ when this == Omniva => onOmniva(),
            _ when this == Sutton => onSutton(),
            _ when this == PantherReference => onPantherReference(),
            _ when this == Sfcservice => onSfcservice(),
            _ when this == Ltl => onLtl(),
            _ when this == Parknparcel => onParknparcel(),
            _ when this == SpringGds => onSpringGds(),
            _ when this == Ecexpress => onEcexpress(),
            _ when this == InterparcelAu => onInterparcelAu(),
            _ when this == Agility => onAgility(),
            _ when this == XlExpress => onXlExpress(),
            _ when this == Aderonline => onAderonline(),
            _ when this == Directcouriers => onDirectcouriers(),
            _ when this == Planzer => onPlanzer(),
            _ when this == Sending => onSending(),
            _ when this == NinjavanWb => onNinjavanWb(),
            _ when this == NationwideMy => onNationwideMy(),
            _ when this == Sendit => onSendit(),
            _ when this == GbArrow => onGbArrow(),
            _ when this == IndGojavas => onIndGojavas(),
            _ when this == Kpost => onKpost(),
            _ when this == DhlFreight => onDhlFreight(),
            _ when this == Bluecare => onBluecare(),
            _ when this == Jindouyun => onJindouyun(),
            _ when this == Trackon => onTrackon(),
            _ when this == GbTuffnells => onGbTuffnells(),
            _ when this == Trumpcard => onTrumpcard(),
            _ when this == Etotal => onEtotal(),
            _ when this == SfplusWebhook => onSfplusWebhook(),
            _ when this == Sekologistics => onSekologistics(),
            _ when this == Hermes2MannHandling => onHermes2MannHandling(),
            _ when this == DpdLocalRef => onDpdLocalRef(),
            _ when this == Uds => onUds(),
            _ when this == ZaSpecialisedFreight => onZaSpecialisedFreight(),
            _ when this == ThaKerry => onThaKerry(),
            _ when this == PrtIntSeur => onPrtIntSeur(),
            _ when this == BraCorreios => onBraCorreios(),
            _ when this == NzNzPost => onNzNzPost(),
            _ when this == CnEquick => onCnEquick(),
            _ when this == MysEms => onMysEms(),
            _ when this == GbNorsk => onGbNorsk(),
            _ when this == EspMrw => onEspMrw(),
            _ when this == EspPacklink => onEspPacklink(),
            _ when this == KangarooMy => onKangarooMy(),
            _ when this == Rpx => onRpx(),
            _ when this == XdpUkReference => onXdpUkReference(),
            _ when this == NinjavanMy => onNinjavanMy(),
            _ when this == Adicional => onAdicional(),
            _ when this == Roadbull => onRoadbull(),
            _ when this == Yakit => onYakit(),
            _ when this == Mailamericas => onMailamericas(),
            _ when this == Mikropakket => onMikropakket(),
            _ when this == Dynalogic => onDynalogic(),
            _ when this == DhlEs => onDhlEs(),
            _ when this == DhlParcelNl => onDhlParcelNl(),
            _ when this == DhlGlobalMailAsia => onDhlGlobalMailAsia(),
            _ when this == DawnWing => onDawnWing(),
            _ when this == GenikiGr => onGenikiGr(),
            _ when this == HermesworldUk => onHermesworldUk(),
            _ when this == Alphafast => onAlphafast(),
            _ when this == Buylogic => onBuylogic(),
            _ when this == Ekart => onEkart(),
            _ when this == MexSenda => onMexSenda(),
            _ when this == SfcLogistics => onSfcLogistics(),
            _ when this == PostSerbia => onPostSerbia(),
            _ when this == IndDelhivery => onIndDelhivery(),
            _ when this == DeDpdDelistrack => onDeDpdDelistrack(),
            _ when this == Rpd2Man => onRpd2Man(),
            _ when this == CnSfExpress => onCnSfExpress(),
            _ when this == Yanwen => onYanwen(),
            _ when this == MysSkynet => onMysSkynet(),
            _ when this == CorreosDeMexico => onCorreosDeMexico(),
            _ when this == CblLogistica => onCblLogistica(),
            _ when this == MexEstafeta => onMexEstafeta(),
            _ when this == AuAustrianPost => onAuAustrianPost(),
            _ when this == Rincos => onRincos(),
            _ when this == NldDhl => onNldDhl(),
            _ when this == RussianPost => onRussianPost(),
            _ when this == CouriersPlease => onCouriersPlease(),
            _ when this == PostnordLogistics => onPostnordLogistics(),
            _ when this == Fedex => onFedex(),
            _ when this == DpeExpress => onDpeExpress(),
            _ when this == Dpd => onDpd(),
            _ when this == Adsone => onAdsone(),
            _ when this == IdnJne => onIdnJne(),
            _ when this == Thecourierguy => onThecourierguy(),
            _ when this == Cnexps => onCnexps(),
            _ when this == PrtChronopost => onPrtChronopost(),
            _ when this == LandmarkGlobal => onLandmarkGlobal(),
            _ when this == ItDhlEcommerce => onItDhlEcommerce(),
            _ when this == EspNacex => onEspNacex(),
            _ when this == PrtCtt => onPrtCtt(),
            _ when this == BeKiala => onBeKiala(),
            _ when this == AsendiaUk => onAsendiaUk(),
            _ when this == GlobalTnt => onGlobalTnt(),
            _ when this == PosturIs => onPosturIs(),
            _ when this == EparcelKr => onEparcelKr(),
            _ when this == InpostPaczkomaty => onInpostPaczkomaty(),
            _ when this == ItPosteItalia => onItPosteItalia(),
            _ when this == BeBpost => onBeBpost(),
            _ when this == PlPocztaPolska => onPlPocztaPolska(),
            _ when this == MysMysPost => onMysMysPost(),
            _ when this == SgSgPost => onSgSgPost(),
            _ when this == ThaThailandPost => onThaThailandPost(),
            _ when this == Lexship => onLexship(),
            _ when this == FastwayNz => onFastwayNz(),
            _ when this == DhlAu => onDhlAu(),
            _ when this == Costmeticsnow => onCostmeticsnow(),
            _ when this == Pflogistics => onPflogistics(),
            _ when this == LoomisExpress => onLoomisExpress(),
            _ when this == GlsItaly => onGlsItaly(),
            _ when this == Line => onLine(),
            _ when this == GelExpress => onGelExpress(),
            _ when this == Huodull => onHuodull(),
            _ when this == NinjavanSg => onNinjavanSg(),
            _ when this == Janio => onJanio(),
            _ when this == AoCourier => onAoCourier(),
            _ when this == BrtItSenderRef => onBrtItSenderRef(),
            _ when this == Sailpost => onSailpost(),
            _ when this == Lalamove => onLalamove(),
            _ when this == NewzealandCouriers => onNewzealandCouriers(),
            _ when this == Etomars => onEtomars(),
            _ when this == Virtransport => onVirtransport(),
            _ when this == Wizmo => onWizmo(),
            _ when this == Palletways => onPalletways(),
            _ when this == IDika => onIDika(),
            _ when this == CflLogistics => onCflLogistics(),
            _ when this == Gemworldwide => onGemworldwide(),
            _ when this == GlobalExpress => onGlobalExpress(),
            _ when this == LogistyxTransgroup => onLogistyxTransgroup(),
            _ when this == WestbankCourier => onWestbankCourier(),
            _ when this == ArcoSpedizioni => onArcoSpedizioni(),
            _ when this == YdhExpress => onYdhExpress(),
            _ when this == Parcelinklogistics => onParcelinklogistics(),
            _ when this == Cndexpress => onCndexpress(),
            _ when this == NoxNightTimeExpress => onNoxNightTimeExpress(),
            _ when this == Aeronet => onAeronet(),
            _ when this == Ltianexp => onLtianexp(),
            _ when this == Integra2Ftp => onIntegra2Ftp(),
            _ when this == Parcelone => onParcelone(),
            _ when this == NoxNachtexpress => onNoxNachtexpress(),
            _ when this == CnChinaPostEms => onCnChinaPostEms(),
            _ when this == Chukou1 => onChukou1(),
            _ when this == GlsSlov => onGlsSlov(),
            _ when this == OrangeDs => onOrangeDs(),
            _ when this == JoomLogis => onJoomLogis(),
            _ when this == AusStartrack => onAusStartrack(),
            _ when this == Dhl => onDhl(),
            _ when this == GbApc => onGbApc(),
            _ when this == Bondscouriers => onBondscouriers(),
            _ when this == JpnJapanPost => onJpnJapanPost(),
            _ when this == Usps => onUsps(),
            _ when this == Winit => onWinit(),
            _ when this == ArgOca => onArgOca(),
            _ when this == TwTaiwanPost => onTwTaiwanPost(),
            _ when this == DmmNetwork => onDmmNetwork(),
            _ when this == Tnt => onTnt(),
            _ when this == BhPosta => onBhPosta(),
            _ when this == SwePostnord => onSwePostnord(),
            _ when this == CaCanadaPost => onCaCanadaPost(),
            _ when this == Wiseloads => onWiseloads(),
            _ when this == AsendiaHk => onAsendiaHk(),
            _ when this == NldGls => onNldGls(),
            _ when this == MexRedpack => onMexRedpack(),
            _ when this == JetShip => onJetShip(),
            _ when this == DeDhlExpress => onDeDhlExpress(),
            _ when this == NinjavanThai => onNinjavanThai(),
            _ when this == RabenGroup => onRabenGroup(),
            _ when this == EspAsm => onEspAsm(),
            _ when this == HrvHrvatska => onHrvHrvatska(),
            _ when this == GlobalEstes => onGlobalEstes(),
            _ when this == LtuLietuvos => onLtuLietuvos(),
            _ when this == BelDhl => onBelDhl(),
            _ when this == AuAuPost => onAuAuPost(),
            _ when this == Speedexcourier => onSpeedexcourier(),
            _ when this == FrColis => onFrColis(),
            _ when this == Aramex => onAramex(),
            _ when this == Dpex => onDpex(),
            _ when this == MysAirpak => onMysAirpak(),
            _ when this == Cuckooexpress => onCuckooexpress(),
            _ when this == DpdPoland => onDpdPoland(),
            _ when this == NldPostnl => onNldPostnl(),
            _ when this == NimExpress => onNimExpress(),
            _ when this == Quantium => onQuantium(),
            _ when this == Sendle => onSendle(),
            _ when this == EspRedur => onEspRedur(),
            _ when this == Matkahuolto => onMatkahuolto(),
            _ when this == Cpacket => onCpacket(),
            _ when this == Posti => onPosti(),
            _ when this == HunterExpress => onHunterExpress(),
            _ when this == ChoirExp => onChoirExp(),
            _ when this == LegionExpress => onLegionExpress(),
            _ when this == AustrianPostExpress => onAustrianPostExpress(),
            _ when this == Grupo => onGrupo(),
            _ when this == PostaRo => onPostaRo(),
            _ when this == InterparcelUk => onInterparcelUk(),
            _ when this == GlobalAbf => onGlobalAbf(),
            _ when this == PostenNorge => onPostenNorge(),
            _ when this == XpertDelivery => onXpertDelivery(),
            _ when this == DhlRefr => onDhlRefr(),
            _ when this == DhlHk => onDhlHk(),
            _ when this == SkynetUae => onSkynetUae(),
            _ when this == Gojek => onGojek(),
            _ when this == YodelIntnl => onYodelIntnl(),
            _ when this == Janco => onJanco(),
            _ when this == Yto => onYto(),
            _ when this == WiseExpress => onWiseExpress(),
            _ when this == JtexpressVn => onJtexpressVn(),
            _ when this == FedexIntlMlserv => onFedexIntlMlserv(),
            _ when this == Vamox => onVamox(),
            _ when this == AmsGrp => onAmsGrp(),
            _ when this == DhlJp => onDhlJp(),
            _ when this == Hrparcel => onHrparcel(),
            _ when this == Geswl => onGeswl(),
            _ when this == Bluestar => onBluestar(),
            _ when this == CdekTr => onCdekTr(),
            _ when this == Descartes => onDescartes(),
            _ when this == DeltecUk => onDeltecUk(),
            _ when this == DtdcExpress => onDtdcExpress(),
            _ when this == Tourline => onTourline(),
            _ when this == BhWorldwide => onBhWorldwide(),
            _ when this == Ocs => onOcs(),
            _ when this == YingnuoLogistics => onYingnuoLogistics(),
            _ when this == Ups => onUps(),
            _ when this == Toll => onToll(),
            _ when this == PrtSeur => onPrtSeur(),
            _ when this == DtdcAu => onDtdcAu(),
            _ when this == ThaDynamicLogistics => onThaDynamicLogistics(),
            _ when this == UbiLogistics => onUbiLogistics(),
            _ when this == FedexCrossborder => onFedexCrossborder(),
            _ when this == A1Post => onA1Post(),
            _ when this == TazmanianFreight => onTazmanianFreight(),
            _ when this == CjIntMy => onCjIntMy(),
            _ when this == SaiaFreight => onSaiaFreight(),
            _ when this == SgQxpress => onSgQxpress(),
            _ when this == NhansSolutions => onNhansSolutions(),
            _ when this == DpdFr => onDpdFr(),
            _ when this == Coordinadora => onCoordinadora(),
            _ when this == Andreani => onAndreani(),
            _ when this == Doora => onDoora(),
            _ when this == InterparcelNz => onInterparcelNz(),
            _ when this == PhlJamexpress => onPhlJamexpress(),
            _ when this == BelBelgiumPost => onBelBelgiumPost(),
            _ when this == UsApc => onUsApc(),
            _ when this == IdnPos => onIdnPos(),
            _ when this == FrMondial => onFrMondial(),
            _ when this == DeDhl => onDeDhl(),
            _ when this == HkRpx => onHkRpx(),
            _ when this == DhlPieceid => onDhlPieceid(),
            _ when this == VnpostEms => onVnpostEms(),
            _ when this == Rrdonnelley => onRrdonnelley(),
            _ when this == DpdDe => onDpdDe(),
            _ when this == DelcartIn => onDelcartIn(),
            _ when this == Imexglobalsolutions => onImexglobalsolutions(),
            _ when this == Acommerce => onAcommerce(),
            _ when this == Eurodis => onEurodis(),
            _ when this == Canpar => onCanpar(),
            _ when this == Gls => onGls(),
            _ when this == IndEcom => onIndEcom(),
            _ when this == EspEnvialia => onEspEnvialia(),
            _ when this == DhlUk => onDhlUk(),
            _ when this == SmsaExpress => onSmsaExpress(),
            _ when this == TntFr => onTntFr(),
            _ when this == DexI => onDexI(),
            _ when this == BudbeeWebhook => onBudbeeWebhook(),
            _ when this == CopaCourier => onCopaCourier(),
            _ when this == VnmVietnamPost => onVnmVietnamPost(),
            _ when this == DpdHk => onDpdHk(),
            _ when this == TollNz => onTollNz(),
            _ when this == Echo => onEcho(),
            _ when this == FedexFr => onFedexFr(),
            _ when this == Borderexpress => onBorderexpress(),
            _ when this == MailplusJpn => onMailplusJpn(),
            _ when this == TntUkRefr => onTntUkRefr(),
            _ when this == Kec => onKec(),
            _ when this == DpdRo => onDpdRo(),
            _ when this == TntJp => onTntJp(),
            _ when this == ThCj => onThCj(),
            _ when this == EcCn => onEcCn(),
            _ when this == FastwayUk => onFastwayUk(),
            _ when this == FastwayUs => onFastwayUs(),
            _ when this == GlsDe => onGlsDe(),
            _ when this == GlsEs => onGlsEs(),
            _ when this == GlsFr => onGlsFr(),
            _ when this == MondialBe => onMondialBe(),
            _ when this == SgtIt => onSgtIt(),
            _ when this == TntCn => onTntCn(),
            _ when this == TntDe => onTntDe(),
            _ when this == TntEs => onTntEs(),
            _ when this == TntPl => onTntPl(),
            _ when this == Parcelforce => onParcelforce(),
            _ when this == SwissPost => onSwissPost(),
            _ when this == TollIpec => onTollIpec(),
            _ when this == Air21 => onAir21(),
            _ when this == Airspeed => onAirspeed(),
            _ when this == Bert => onBert(),
            _ when this == Bluedart => onBluedart(),
            _ when this == Collectplus => onCollectplus(),
            _ when this == Courierplus => onCourierplus(),
            _ when this == CourierPost => onCourierPost(),
            _ when this == DhlGlobalMail => onDhlGlobalMail(),
            _ when this == DpdUk => onDpdUk(),
            _ when this == DeltecDe => onDeltecDe(),
            _ when this == DeutscheDe => onDeutscheDe(),
            _ when this == Dotzot => onDotzot(),
            _ when this == EltaGr => onEltaGr(),
            _ when this == EmsCn => onEmsCn(),
            _ when this == Ecargo => onEcargo(),
            _ when this == Ensenda => onEnsenda(),
            _ when this == FercamIt => onFercamIt(),
            _ when this == FastwayZa => onFastwayZa(),
            _ when this == FastwayAu => onFastwayAu(),
            _ when this == FirstLogisitcs => onFirstLogisitcs(),
            _ when this == Geodis => onGeodis(),
            _ when this == Globegistics => onGlobegistics(),
            _ when this == Greyhound => onGreyhound(),
            _ when this == JetshipMy => onJetshipMy(),
            _ when this == LionParcel => onLionParcel(),
            _ when this == Aeroflash => onAeroflash(),
            _ when this == Ontrac => onOntrac(),
            _ when this == Sagawa => onSagawa(),
            _ when this == Siodemka => onSiodemka(),
            _ when this == Startrack => onStartrack(),
            _ when this == TntAu => onTntAu(),
            _ when this == TntIt => onTntIt(),
            _ when this == Transmission => onTransmission(),
            _ when this == Yamato => onYamato(),
            _ when this == DhlIt => onDhlIt(),
            _ when this == DhlAt => onDhlAt(),
            _ when this == LogisticsworldwideKr => onLogisticsworldwideKr(),
            _ when this == GlsSpain => onGlsSpain(),
            _ when this == AmazonUkApi => onAmazonUkApi(),
            _ when this == DpdFrReference => onDpdFrReference(),
            _ when this == DhlparcelUk => onDhlparcelUk(),
            _ when this == Megasave => onMegasave(),
            _ when this == Qualitypost => onQualitypost(),
            _ when this == IdsLogistics => onIdsLogistics(),
            _ when this == Joyingbox => onJoyingbox(),
            _ when this == PantherOrderNumber => onPantherOrderNumber(),
            _ when this == WatkinsShepard => onWatkinsShepard(),
            _ when this == Fasttrack => onFasttrack(),
            _ when this == UpExpress => onUpExpress(),
            _ when this == Elogistica => onElogistica(),
            _ when this == Ecourier => onEcourier(),
            _ when this == CjPhilippines => onCjPhilippines(),
            _ when this == Speedex => onSpeedex(),
            _ when this == Orangeconnex => onOrangeconnex(),
            _ when this == Tecor => onTecor(),
            _ when this == Saee => onSaee(),
            _ when this == GlsItalyFtp => onGlsItalyFtp(),
            _ when this == Delivere => onDelivere(),
            _ when this == Yycom => onYycom(),
            _ when this == AdicionalPt => onAdicionalPt(),
            _ when this == Dksh => onDksh(),
            _ when this == NipponExpressFtp => onNipponExpressFtp(),
            _ when this == Gols => onGols(),
            _ when this == Fujexp => onFujexp(),
            _ when this == Qtrack => onQtrack(),
            _ when this == OmlogisticsApi => onOmlogisticsApi(),
            _ when this == Gdpharm => onGdpharm(),
            _ when this == MisumiCn => onMisumiCn(),
            _ when this == AirCanada => onAirCanada(),
            _ when this == City56Webhook => onCity56Webhook(),
            _ when this == SagawaApi => onSagawaApi(),
            _ when this == Kedaex => onKedaex(),
            _ when this == PgeonApi => onPgeonApi(),
            _ when this == Weworldexpress => onWeworldexpress(),
            _ when this == JtLogistics => onJtLogistics(),
            _ when this == Trusk => onTrusk(),
            _ when this == Viaxpress => onViaxpress(),
            _ when this == DhlSupplychainId => onDhlSupplychainId(),
            _ when this == ZuelligpharmaSftp => onZuelligpharmaSftp(),
            _ when this == Meest => onMeest(),
            _ when this == TollPriority => onTollPriority(),
            _ when this == MothershipApi => onMothershipApi(),
            _ when this == Capital => onCapital(),
            _ when this == EuropaketApi => onEuropaketApi(),
            _ when this == Hfd => onHfd(),
            _ when this == TourlineReference => onTourlineReference(),
            _ when this == GioEcourier => onGioEcourier(),
            _ when this == CnLogistics => onCnLogistics(),
            _ when this == Pandion => onPandion(),
            _ when this == BpostApi => onBpostApi(),
            _ when this == Passportshipping => onPassportshipping(),
            _ when this == Pakajo => onPakajo(),
            _ when this == Dachser => onDachser(),
            _ when this == YusenSftp => onYusenSftp(),
            _ when this == Shyplite => onShyplite(),
            _ when this == Xyy => onXyy(),
            _ when this == Mwd => onMwd(),
            _ when this == Faxecargo => onFaxecargo(),
            _ when this == Mazet => onMazet(),
            _ when this == FirstLogisticsApi => onFirstLogisticsApi(),
            _ when this == SprintPack => onSprintPack(),
            _ when this == HermesDeFtp => onHermesDeFtp(),
            _ when this == Concise => onConcise(),
            _ when this == KerryExpressTwApi => onKerryExpressTwApi(),
            _ when this == Ewe => onEwe(),
            _ when this == Fastdespatch => onFastdespatch(),
            _ when this == AbcustomSftp => onAbcustomSftp(),
            _ when this == Chazki => onChazki(),
            _ when this == Shippie => onShippie(),
            _ when this == GeodisApi => onGeodisApi(),
            _ when this == NaqelExpress => onNaqelExpress(),
            _ when this == PapaWebhook => onPapaWebhook(),
            _ when this == Forwardair => onForwardair(),
            _ when this == DialogoLogisticaApi => onDialogoLogisticaApi(),
            _ when this == LalamoveApi => onLalamoveApi(),
            _ when this == Tomydoor => onTomydoor(),
            _ when this == KronosWebhook => onKronosWebhook(),
            _ when this == Jtcargo => onJtcargo(),
            _ when this == TCat => onTCat(),
            _ when this == ConciseWebhook => onConciseWebhook(),
            _ when this == TeleportWebhook => onTeleportWebhook(),
            _ when this == CustomcoApi => onCustomcoApi(),
            _ when this == SpxTh => onSpxTh(),
            _ when this == BolloreLogistics => onBolloreLogistics(),
            _ when this == ClicklinkSftp => onClicklinkSftp(),
            _ when this == M3Logistics => onM3Logistics(),
            _ when this == VnpostApi => onVnpostApi(),
            _ when this == AxlehireFtp => onAxlehireFtp(),
            _ when this == Shadowfax => onShadowfax(),
            _ when this == MyhermesUkApi => onMyhermesUkApi(),
            _ when this == Daiichi => onDaiichi(),
            _ when this == MensajerosurbanosApi => onMensajerosurbanosApi(),
            _ when this == Polarspeed => onPolarspeed(),
            _ when this == IdexpressId => onIdexpressId(),
            _ when this == Payo => onPayo(),
            _ when this == WhistlSftp => onWhistlSftp(),
            _ when this == IntexDe => onIntexDe(),
            _ when this == Trans2U => onTrans2U(),
            _ when this == ProductcaregroupSftp => onProductcaregroupSftp(),
            _ when this == Bigsmart => onBigsmart(),
            _ when this == ExpeditorsApiRef => onExpeditorsApiRef(),
            _ when this == AitworldwideApi => onAitworldwideApi(),
            _ when this == Worldcourier => onWorldcourier(),
            _ when this == Quiqup => onQuiqup(),
            _ when this == AgedissSftp => onAgedissSftp(),
            _ when this == AndreaniApi => onAndreaniApi(),
            _ when this == Crlexpress => onCrlexpress(),
            _ when this == Smartcat => onSmartcat(),
            _ when this == Crossflight => onCrossflight(),
            _ when this == Procarrier => onProcarrier(),
            _ when this == DhlReferenceApi => onDhlReferenceApi(),
            _ when this == SeinoApi => onSeinoApi(),
            _ when this == Wspexpress => onWspexpress(),
            _ when this == Kronos => onKronos(),
            _ when this == TotalExpressApi => onTotalExpressApi(),
            _ when this == Parcll => onParcll(),
            _ when this == Xpedigo => onXpedigo(),
            _ when this == StarTrackWebhook => onStarTrackWebhook(),
            _ when this == Gpost => onGpost(),
            _ when this == Ucs => onUcs(),
            _ when this == Dmfgroup => onDmfgroup(),
            _ when this == CoordinadoraApi => onCoordinadoraApi(),
            _ when this == Marken => onMarken(),
            _ when this == Ntl => onNtl(),
            _ when this == Redjepakketje => onRedjepakketje(),
            _ when this == AlliedExpressFtp => onAlliedExpressFtp(),
            _ when this == MondialrelayEs => onMondialrelayEs(),
            _ when this == NaekoFtp => onNaekoFtp(),
            _ when this == Mhi => onMhi(),
            _ when this == Shippify => onShippify(),
            _ when this == MalcaAmitApi => onMalcaAmitApi(),
            _ when this == JtexpressSgApi => onJtexpressSgApi(),
            _ when this == DachserWeb => onDachserWeb(),
            _ when this == Flightlg => onFlightlg(),
            _ when this == Cago => onCago(),
            _ when this == Com1Express => onCom1Express(),
            _ when this == TonamiFtp => onTonamiFtp(),
            _ when this == Packfleet => onPackfleet(),
            _ when this == PurolatorInternational => onPurolatorInternational(),
            _ when this == WineshippingWebhook => onWineshippingWebhook(),
            _ when this == DhlEsSftp => onDhlEsSftp(),
            _ when this == PchomeApi => onPchomeApi(),
            _ when this == CeskapostaApi => onCeskapostaApi(),
            _ when this == Gorush => onGorush(),
            _ when this == Homerunner => onHomerunner(),
            _ when this == AmazonOrder => onAmazonOrder(),
            _ when this == EfwnowApi => onEfwnowApi(),
            _ when this == CblLogisticaApi => onCblLogisticaApi(),
            _ when this == Nimbuspost => onNimbuspost(),
            _ when this == LogwinLogistics => onLogwinLogistics(),
            _ when this == NowlogApi => onNowlogApi(),
            _ when this == DpdNl => onDpdNl(),
            _ when this == Godependable => onGodependable(),
            _ when this == Esdex => onEsdex(),
            _ when this == LogisystemsSftp => onLogisystemsSftp(),
            _ when this == Expeditors => onExpeditors(),
            _ when this == SntglobalApi => onSntglobalApi(),
            _ when this == Shipx => onShipx(),
            _ when this == QintlApi => onQintlApi(),
            _ when this == Packs => onPacks(),
            _ when this == PostnlInternational => onPostnlInternational(),
            _ when this == AmazonEmailPush => onAmazonEmailPush(),
            _ when this == DhlApi => onDhlApi(),
            _ when this == Spx => onSpx(),
            _ when this == Axlehire => onAxlehire(),
            _ when this == Icscourier => onIcscourier(),
            _ when this == DialogoLogistica => onDialogoLogistica(),
            _ when this == ShunbangExpress => onShunbangExpress(),
            _ when this == TcsApi => onTcsApi(),
            _ when this == SfExpressCn => onSfExpressCn(),
            _ when this == Packeta => onPacketa(),
            _ when this == SicTeliway => onSicTeliway(),
            _ when this == MondialrelayFr => onMondialrelayFr(),
            _ when this == IntimeFtp => onIntimeFtp(),
            _ when this == JdExpress => onJdExpress(),
            _ when this == Fastbox => onFastbox(),
            _ when this == Patheon => onPatheon(),
            _ when this == IndiaPost => onIndiaPost(),
            _ when this == TipsaRef => onTipsaRef(),
            _ when this == Ecofreight => onEcofreight(),
            _ when this == Vox => onVox(),
            _ when this == DirectfreightAuRef => onDirectfreightAuRef(),
            _ when this == BesttransportSftp => onBesttransportSftp(),
            _ when this == AustraliaPostApi => onAustraliaPostApi(),
            _ when this == FragilepakSftp => onFragilepakSftp(),
            _ when this == Flipxp => onFlipxp(),
            _ when this == ValueWebhook => onValueWebhook(),
            _ when this == Daeshin => onDaeshin(),
            _ when this == Sherpa => onSherpa(),
            _ when this == MwdApi => onMwdApi(),
            _ when this == Smartkargo => onSmartkargo(),
            _ when this == DnjExpress => onDnjExpress(),
            _ when this == Gopeople => onGopeople(),
            _ when this == MysendleApi => onMysendleApi(),
            _ when this == AramexApi => onAramexApi(),
            _ when this == Pidge => onPidge(),
            _ when this == Thaiparcels => onThaiparcels(),
            _ when this == PantherReferenceApi => onPantherReferenceApi(),
            _ when this == Postaplus => onPostaplus(),
            _ when this == Buffalo => onBuffalo(),
            _ when this == UEnvios => onUEnvios(),
            _ when this == EliteCo => onEliteCo(),
            _ when this == RocheInternalSftp => onRocheInternalSftp(),
            _ when this == DbschenkerIceland => onDbschenkerIceland(),
            _ when this == TntFrReference => onTntFrReference(),
            _ when this == Newgisticsapi => onNewgisticsapi(),
            _ when this == Glovo => onGlovo(),
            _ when this == GwlogisApi => onGwlogisApi(),
            _ when this == SpreetailApi => onSpreetailApi(),
            _ when this == Moova => onMoova(),
            _ when this == Plycongroup => onPlycongroup(),
            _ when this == UspsWebhook => onUspsWebhook(),
            _ when this == Reimaginedelivery => onReimaginedelivery(),
            _ when this == EdfFtp => onEdfFtp(),
            _ when this == Dao365 => onDao365(),
            _ when this == BiocairFtp => onBiocairFtp(),
            _ when this == RansaWebhook => onRansaWebhook(),
            _ when this == Shipxpres => onShipxpres(),
            _ when this == CourantPlusApi => onCourantPlusApi(),
            _ when this == Shipa => onShipa(),
            _ when this == Homelogistics => onHomelogistics(),
            _ when this == Dx => onDx(),
            _ when this == PosteItalianePaccocelere => onPosteItalianePaccocelere(),
            _ when this == TollWebhook => onTollWebhook(),
            _ when this == LctbrApi => onLctbrApi(),
            _ when this == DxFreight => onDxFreight(),
            _ when this == DhlSftp => onDhlSftp(),
            _ when this == Shiprocket => onShiprocket(),
            _ when this == UberWebhook => onUberWebhook(),
            _ when this == Statovernight => onStatovernight(),
            _ when this == Burd => onBurd(),
            _ when this == Fastship => onFastship(),
            _ when this == IbventureWebhook => onIbventureWebhook(),
            _ when this == GatiKweApi => onGatiKweApi(),
            _ when this == CryopdpFtp => onCryopdpFtp(),
            _ when this == Hubbed => onHubbed(),
            _ when this == TipsaApi => onTipsaApi(),
            _ when this == Araskargo => onAraskargo(),
            _ when this == ThijsNl => onThijsNl(),
            _ when this == AtshealthcareReference => onAtshealthcareReference(),
            _ when this == _99Minutos => on_99Minutos(),
            _ when this == HellenicPost => onHellenicPost(),
            _ when this == HsmGlobal => onHsmGlobal(),
            _ when this == Mnx => onMnx(),
            _ when this == Nmtransfer => onNmtransfer(),
            _ when this == Logysto => onLogysto(),
            _ when this == IndiaPostInt => onIndiaPostInt(),
            _ when this == AmazonFbaSwishipIn => onAmazonFbaSwishipIn(),
            _ when this == SrtTransport => onSrtTransport(),
            _ when this == Bomi => onBomi(),
            _ when this == DeliverrSftp => onDeliverrSftp(),
            _ when this == Hsdexpress => onHsdexpress(),
            _ when this == SimpletireWebhook => onSimpletireWebhook(),
            _ when this == HunterExpressSftp => onHunterExpressSftp(),
            _ when this == UpsApi => onUpsApi(),
            _ when this == WooyoungLogisticsSftp => onWooyoungLogisticsSftp(),
            _ when this == PhseApi => onPhseApi(),
            _ when this == WishEmailPush => onWishEmailPush(),
            _ when this == Northline => onNorthline(),
            _ when this == Medafrica => onMedafrica(),
            _ when this == DpdAtSftp => onDpdAtSftp(),
            _ when this == Anteraja => onAnteraja(),
            _ when this == DhlGlobalForwardingApi => onDhlGlobalForwardingApi(),
            _ when this == LbcexpressApi => onLbcexpressApi(),
            _ when this == Simsglobal => onSimsglobal(),
            _ when this == Cdldelivers => onCdldelivers(),
            _ when this == Typ => onTyp(),
            _ when this == TestingCourierWebhook => onTestingCourierWebhook(),
            _ when this == PandagoApi => onPandagoApi(),
            _ when this == RoyalMailFtp => onRoyalMailFtp(),
            _ when this == Thunderexpress => onThunderexpress(),
            _ when this == SecretlabWebhook => onSecretlabWebhook(),
            _ when this == Setel => onSetel(),
            _ when this == JdWorldwide => onJdWorldwide(),
            _ when this == DpdRuApi => onDpdRuApi(),
            _ when this == ArgentsWebhook => onArgentsWebhook(),
            _ when this == Postone => onPostone(),
            _ when this == Tusklogistics => onTusklogistics(),
            _ when this == RhenusUkApi => onRhenusUkApi(),
            _ when this == TaqbinSgApi => onTaqbinSgApi(),
            _ when this == InntralogSftp => onInntralogSftp(),
            _ when this == Dayross => onDayross(),
            _ when this == CorreosexpressApi => onCorreosexpressApi(),
            _ when this == InternationalSeurApi => onInternationalSeurApi(),
            _ when this == YodelApi => onYodelApi(),
            _ when this == Heroexpress => onHeroexpress(),
            _ when this == DhlSupplychainIn => onDhlSupplychainIn(),
            _ when this == UrgentCargus => onUrgentCargus(),
            _ when this == Frontdoorcorp => onFrontdoorcorp(),
            _ when this == JtexpressPh => onJtexpressPh(),
            _ when this == ParcelstarsWebhook => onParcelstarsWebhook(),
            _ when this == DpdSkSftp => onDpdSkSftp(),
            _ when this == Movianto => onMovianto(),
            _ when this == OzepartsShipping => onOzepartsShipping(),
            _ when this == Kargomkolay => onKargomkolay(),
            _ when this == Trunkrs => onTrunkrs(),
            _ when this == OmnirpsWebhook => onOmnirpsWebhook(),
            _ when this == Chilexpress => onChilexpress(),
            _ when this == TestingCourier => onTestingCourier(),
            _ when this == JneApi => onJneApi(),
            _ when this == BjshomedeliveryFtp => onBjshomedeliveryFtp(),
            _ when this == DexpressWebhook => onDexpressWebhook(),
            _ when this == UspsApi => onUspsApi(),
            _ when this == Transvirtual => onTransvirtual(),
            _ when this == SolisticaApi => onSolisticaApi(),
            _ when this == ChienventureWebhook => onChienventureWebhook(),
            _ when this == DpdUkSftp => onDpdUkSftp(),
            _ when this == InpostUk => onInpostUk(),
            _ when this == Javit => onJavit(),
            _ when this == ZtoDomestic => onZtoDomestic(),
            _ when this == DhlGtApi => onDhlGtApi(),
            _ when this == CevaTracking => onCevaTracking(),
            _ when this == KomonExpress => onKomonExpress(),
            _ when this == EastwestcourierFtp => onEastwestcourierFtp(),
            _ when this == Danniao => onDanniao(),
            _ when this == Spectran => onSpectran(),
            _ when this == DeliverIt => onDeliverIt(),
            _ when this == Relaiscolis => onRelaiscolis(),
            _ when this == GlsSpainApi => onGlsSpainApi(),
            _ when this == Postplus => onPostplus(),
            _ when this == Airterra => onAirterra(),
            _ when this == GioEcourierApi => onGioEcourierApi(),
            _ when this == DpdChSftp => onDpdChSftp(),
            _ when this == FedexApi => onFedexApi(),
            _ when this == Intersmarttrans => onIntersmarttrans(),
            _ when this == HermesUkSftp => onHermesUkSftp(),
            _ when this == ExelotFtp => onExelotFtp(),
            _ when this == DhlPaApi => onDhlPaApi(),
            _ when this == VirtransportSftp => onVirtransportSftp(),
            _ when this == Worldnet => onWorldnet(),
            _ when this == InstaboxWebhook => onInstaboxWebhook(),
            _ when this == Kng => onKng(),
            _ when this == FlashexpressWebhook => onFlashexpressWebhook(),
            _ when this == MagyarPostaApi => onMagyarPostaApi(),
            _ when this == WeshipApi => onWeshipApi(),
            _ when this == OhiWebhook => onOhiWebhook(),
            _ when this == Mudita => onMudita(),
            _ when this == BluedartApi => onBluedartApi(),
            _ when this == TCatApi => onTCatApi(),
            _ when this == Ads => onAds(),
            _ when this == HermesIt => onHermesIt(),
            _ when this == FitzmarkApi => onFitzmarkApi(),
            _ when this == PostiApi => onPostiApi(),
            _ when this == SmsaExpressWebhook => onSmsaExpressWebhook(),
            _ when this == TamergroupWebhook => onTamergroupWebhook(),
            _ when this == Livrapide => onLivrapide(),
            _ when this == NipponExpress => onNipponExpress(),
            _ when this == Bettertrucks => onBettertrucks(),
            _ when this == Fan => onFan(),
            _ when this == PbUspsflatsFtp => onPbUspsflatsFtp(),
            _ when this == Parcelright => onParcelright(),
            _ when this == Ithinklogistics => onIthinklogistics(),
            _ when this == KerryExpressThWebhook => onKerryExpressThWebhook(),
            _ when this == Ecoutier => onEcoutier(),
            _ when this == Showl => onShowl(),
            _ when this == BrtItApi => onBrtItApi(),
            _ when this == RixonhkApi => onRixonhkApi(),
            _ when this == DbschenkerApi => onDbschenkerApi(),
            _ when this == Ilyanglogis => onIlyanglogis(),
            _ when this == MailBoxEtc => onMailBoxEtc(),
            _ when this == Weship => onWeship(),
            _ when this == DhlGlobalMailApi => onDhlGlobalMailApi(),
            _ when this == Activos24Api => onActivos24Api(),
            _ when this == Atshealthcare => onAtshealthcare(),
            _ when this == Luwjistik => onLuwjistik(),
            _ when this == GwWorld => onGwWorld(),
            _ when this == FairsendenApi => onFairsendenApi(),
            _ when this == ServipWebhook => onServipWebhook(),
            _ when this == Swiship => onSwiship(),
            _ when this == Tanet => onTanet(),
            _ when this == HotsinCargo => onHotsinCargo(),
            _ when this == Direx => onDirex(),
            _ when this == Huantong => onHuantong(),
            _ when this == ImileApi => onImileApi(),
            _ when this == Auexpress => onAuexpress(),
            _ when this == Nytlogistics => onNytlogistics(),
            _ when this == DsvReference => onDsvReference(),
            _ when this == NovofarmaWebhook => onNovofarmaWebhook(),
            _ when this == AitworldwideSftp => onAitworldwideSftp(),
            _ when this == Shopolive => onShopolive(),
            _ when this == FnfZa => onFnfZa(),
            _ when this == DhlEcommerceGc => onDhlEcommerceGc(),
            _ when this == Fetchr => onFetchr(),
            _ when this == StarlinksApi => onStarlinksApi(),
            _ when this == Yyexpress => onYyexpress(),
            _ when this == Servientrega => onServientrega(),
            _ when this == Hanjin => onHanjin(),
            _ when this == SpanishSeurFtp => onSpanishSeurFtp(),
            _ when this == DxB2BConnum => onDxB2BConnum(),
            _ when this == HelthjemApi => onHelthjemApi(),
            _ when this == Inexpost => onInexpost(),
            _ when this == A2BBa => onA2BBa(),
            _ when this == RhenusGroup => onRhenusGroup(),
            _ when this == SberlogisticsRu => onSberlogisticsRu(),
            _ when this == MalcaAmit => onMalcaAmit(),
            _ when this == Ppl => onPpl(),
            _ when this == OsmWorldwideSftp => onOsmWorldwideSftp(),
            _ when this == Acilogistix => onAcilogistix(),
            _ when this == Optimacourier => onOptimacourier(),
            _ when this == NovaPoshtaApi => onNovaPoshtaApi(),
            _ when this == Loggi => onLoggi(),
            _ when this == Yifan => onYifan(),
            _ when this == Mydynalogic => onMydynalogic(),
            _ when this == Morninglobal => onMorninglobal(),
            _ when this == ConciseApi => onConciseApi(),
            _ when this == Fxtran => onFxtran(),
            _ when this == DeliveryourparcelZa => onDeliveryourparcelZa(),
            _ when this == Uparcel => onUparcel(),
            _ when this == MobiBr => onMobiBr(),
            _ when this == LoginextWebhook => onLoginextWebhook(),
            _ when this == Ems => onEms(),
            _ when this == Speedy => onSpeedy(),
            _ when this == ZoomRed => onZoomRed(),
            _ when this == Navlungo => onNavlungo(),
            _ when this == Castleparcels => onCastleparcels(),
            _ when this == Weee => onWeee(),
            _ when this == Packaly => onPackaly(),
            _ when this == Yunhuipost => onYunhuipost(),
            _ when this == Youparcel => onYouparcel(),
            _ when this == Leman => onLeman(),
            _ when this == Moovin => onMoovin(),
            _ when this == UrbIt => onUrbIt(),
            _ when this == Multientregapanama => onMultientregapanama(),
            _ when this == Jusdasr => onJusdasr(),
            _ when this == Discountpost => onDiscountpost(),
            _ when this == RhenusUk => onRhenusUk(),
            _ when this == SwishipJp => onSwishipJp(),
            _ when this == GlsUs => onGlsUs(),
            _ when this == Smtl => onSmtl(),
            _ when this == Emega => onEmega(),
            _ when this == ExpressoneSv => onExpressoneSv(),
            _ when this == Hepsijet => onHepsijet(),
            _ when this == Welivery => onWelivery(),
            _ when this == Bringer => onBringer(),
            _ when this == Easyroutes => onEasyroutes(),
            _ when this == Mrw => onMrw(),
            _ when this == Rpm => onRpm(),
            _ when this == DpdPrt => onDpdPrt(),
            _ when this == GlsRomania => onGlsRomania(),
            _ when this == Lmparcel => onLmparcel(),
            _ when this == Gtagsm => onGtagsm(),
            _ when this == Domino => onDomino(),
            _ when this == Eshipper => onEshipper(),
            _ when this == Transpak => onTranspak(),
            _ when this == Xindus => onXindus(),
            _ when this == Aoyue => onAoyue(),
            _ when this == Easyparcel => onEasyparcel(),
            _ when this == Expressone => onExpressone(),
            _ when this == SendeoKargo => onSendeoKargo(),
            _ when this == Speedaf => onSpeedaf(),
            _ when this == Etower => onEtower(),
            _ when this == Gcx => onGcx(),
            _ when this == NinjavanVn => onNinjavanVn(),
            _ when this == Allegro => onAllegro(),
            _ when this == Jumppoint => onJumppoint(),
            _ when this == ShipglobalUs => onShipglobalUs(),
            _ when this == Kinisi => onKinisi(),
            _ when this == Oakh => onOakh(),
            _ when this == Awest => onAwest(),
            _ when this == Barsan => onBarsan(),
            _ when this == Energologistic => onEnergologistic(),
            _ when this == Madrooex => onMadrooex(),
            _ when this == Gobolt => onGobolt(),
            _ when this == SwissUniversalExpress => onSwissUniversalExpress(),
            _ when this == Iordirect => onIordirect(),
            _ when this == Xmszm => onXmszm(),
            _ when this == GlsHun => onGlsHun(),
            _ when this == Sendy => onSendy(),
            _ when this == Braunsexpress => onBraunsexpress(),
            _ when this == Grandslamexpress => onGrandslamexpress(),
            _ when this == Xgs => onXgs(),
            _ when this == Otschile => onOtschile(),
            _ when this == PackUp => onPackUp(),
            _ when this == Parcelstars => onParcelstars(),
            _ when this == Teamexpressllc => onTeamexpressllc(),
            _ when this == Asyadexpress => onAsyadexpress(),
            _ when this == Tdn => onTdn(),
            _ when this == Earlybird => onEarlybird(),
            _ when this == Cacesa => onCacesa(),
            _ when this == Parceljet => onParceljet(),
            _ when this == MngKargo => onMngKargo(),
            _ when this == Superpackline => onSuperpackline(),
            _ when this == Speedx => onSpeedx(),
            _ when this == Vesyl => onVesyl(),
            _ when this == Skyking => onSkyking(),
            _ when this == Dirmensajeria => onDirmensajeria(),
            _ when this == Netlogixgroup => onNetlogixgroup(),
            _ when this == Zyou => onZyou(),
            _ when this == Jawar => onJawar(),
            _ when this == Agsystems => onAgsystems(),
            _ when this == Gps => onGps(),
            _ when this == PttKargo => onPttKargo(),
            _ when this == Maergo => onMaergo(),
            _ when this == Arihantcourier => onArihantcourier(),
            _ when this == Vtfe => onVtfe(),
            _ when this == Yunant => onYunant(),
            _ when this == Urbify => onUrbify(),
            _ when this == PackMan => onPackMan(),
            _ when this == Liefergrun => onLiefergrun(),
            _ when this == Obibox => onObibox(),
            _ when this == Paikeda => onPaikeda(),
            _ when this == Scotty => onScotty(),
            _ when this == IntelcomCa => onIntelcomCa(),
            _ when this == Swe => onSwe(),
            _ when this == Asendia => onAsendia(),
            _ when this == DpdAt => onDpdAt(),
            _ when this == Relay => onRelay(),
            _ when this == Ata => onAta(),
            _ when this == SkyexpressInternational => onSkyexpressInternational(),
            _ when this == SuratKargo => onSuratKargo(),
            _ when this == Sglink => onSglink(),
            _ when this == Fleetopticsinc => onFleetopticsinc(),
            _ when this == Shopline => onShopline(),
            _ when this == Piggyship => onPiggyship(),
            _ when this == Logoix => onLogoix(),
            _ when this == KolayGelsin => onKolayGelsin(),
            _ when this == AssociatedCouriers => onAssociatedCouriers(),
            _ when this == UpsChecker => onUpsChecker(),
            _ when this == Wineshipping => onWineshipping(),
            _ when this == Spedisci => onSpedisci(),
            _ when this == Fourkites => onFourkites(),
            _ when this == Etonas => onEtonas(),
            _ when this == Finmile => onFinmile(),
            _ when this == Uniuni => onUniuni(),
            _ when this == Rodonaves => onRodonaves(),
            _ when this == InpostIt => onInpostIt(),
            _ when this == TforceFreight => onTforceFreight(),
            _ when this == Richmom => onRichmom(),
            _ when this == Franco => onFranco(),
            _ when this == Ecparcel => onEcparcel(),
            _ when this == FedexChina => onFedexChina(),
            _ when this == GofoExpress => onGofoExpress(),
            _ when this == Shipbob => onShipbob(),
            _ when this == JerseypostAtlas => onJerseypostAtlas(),
            _ when this == Coretrails => onCoretrails(),
            _ when this == RhenusItaly => onRhenusItaly(),
            _ when this == Jadlog => onJadlog(),
            _ when this == Jitsu => onJitsu(),
            _ when this == YanwenExpress => onYanwenExpress(),
            _ when this == Dashlink => onDashlink(),
            _ when this == SeinoSuperExpress => onSeinoSuperExpress(),
            _ when this == Floship => onFloship(),
            _ when this == Metroscg => onMetroscg(),
            _ when this == Sendparcel => onSendparcel(),
            _ when this == P2P => onP2P(),
            _ when this == CnExpress => onCnExpress(),
            _ when this == Cirrotrack => onCirrotrack(),
            _ when this == LandLogistics => onLandLogistics(),
            _ when this == Veho => onVeho(),
            _ when this == Medline => onMedline(),
            _ when this == Vdtrack => onVdtrack(),
            _ when this == SinoScm => onSinoScm(),
            _ when this == _3PeExpress => on_3PeExpress(),
            _ when this == Swiftx => onSwiftx(),
            _ when this == Sfydexpress => onSfydexpress(),
            _ when this == Toptrans => onToptrans(),
            _ when this == Other => onOther(),
            _ => otherwise(Value)
        };

    public void Match(Action onDpdRu,
        Action onBgBulgarianPost,
        Action onKrKoreaPost,
        Action onZaCourierit,
        Action onFrExapaq,
        Action onAreEmiratesPost,
        Action onGac,
        Action onGeis,
        Action onSfEx,
        Action onPago,
        Action onMyhermes,
        Action onDiamondEurogistics,
        Action onCorporatecouriersWebhook,
        Action onBond,
        Action onOmniparcel,
        Action onSkPosta,
        Action onPurolator,
        Action onFetchrWebhook,
        Action onThedeliverygroup,
        Action onCelloSquare,
        Action onTarrive,
        Action onCollivery,
        Action onMainfreight,
        Action onIndFirstflight,
        Action onAcsworldwide,
        Action onAmstan,
        Action onOkayparcel,
        Action onEnvialiaReference,
        Action onSeurEs,
        Action onContinental,
        Action onFdsexpress,
        Action onAmazonFbaSwiship,
        Action onWyngs,
        Action onDhlActiveTracing,
        Action onZyllem,
        Action onRuston,
        Action onXpost,
        Action onCorreosEs,
        Action onDhlFr,
        Action onPanAsia,
        Action onBrtIt,
        Action onSreKorea,
        Action onSpeedee,
        Action onTntUk,
        Action onVenipak,
        Action onShreenandancourier,
        Action onCroshot,
        Action onNipostNg,
        Action onEpstGlbl,
        Action onNewgistics,
        Action onPostSlovenia,
        Action onJerseyPost,
        Action onBombinoexp,
        Action onWmg,
        Action onXqExpress,
        Action onFurdeco,
        Action onLhtExpress,
        Action onSouthAfricanPostOffice,
        Action onSpoton,
        Action onDimerco,
        Action onCyprusPostCyp,
        Action onAbcustom,
        Action onIndDelivree,
        Action onCnBestexpress,
        Action onDxSftp,
        Action onPickuppMys,
        Action onFmx,
        Action onHellmann,
        Action onShipItAsia,
        Action onKerryEcommerce,
        Action onFreterapido,
        Action onPitneyBowes,
        Action onXpressenDk,
        Action onSeurSpApi,
        Action onDeliveryontime,
        Action onJinsung,
        Action onTransKargo,
        Action onSwishipDe,
        Action onIvoyWebhook,
        Action onAirmeeWebhook,
        Action onDhlBenelux,
        Action onFirstmile,
        Action onFastwayIr,
        Action onHhExp,
        Action onMysMypostOnline,
        Action onTntNl,
        Action onTipsa,
        Action onTaqbinMy,
        Action onKgmhub,
        Action onIntexpress,
        Action onOverseExp,
        Action onOneclick,
        Action onRoadrunnerFreight,
        Action onGlsCrotia,
        Action onMrwFtp,
        Action onBluex,
        Action onDylt,
        Action onDpdIr,
        Action onSinGlbl,
        Action onTuffnellsReference,
        Action onCjpacket,
        Action onMilkman,
        Action onAsigna,
        Action onOneworldexpress,
        Action onRoyalMail,
        Action onViaExpress,
        Action onTigfreight,
        Action onZtoExpress,
        Action onTwoGo,
        Action onIml,
        Action onIntelValley,
        Action onEfs,
        Action onUkUkMail,
        Action onRam,
        Action onAlliedexpress,
        Action onApcOvernight,
        Action onShippit,
        Action onTfm,
        Action onMXpress,
        Action onHdbBox,
        Action onClevyLinks,
        Action onIbeone,
        Action onFiegeNl,
        Action onKweGlobal,
        Action onCtcExpress,
        Action onAmazon,
        Action onMoreLink,
        Action onJx,
        Action onEasyMail,
        Action onAduiepyle,
        Action onGbPanther,
        Action onExpresssale,
        Action onSgDetrack,
        Action onTrunkrsWebhook,
        Action onMatdespatch,
        Action onDicom,
        Action onMbw,
        Action onKhmCambodiaPost,
        Action onSinotrans,
        Action onBrtItParcelid,
        Action onDhlSupplyChain,
        Action onDhlPl,
        Action onTopyou,
        Action onPalexpress,
        Action onDhlSg,
        Action onCnWedo,
        Action onFulfillme,
        Action onDpdDelistrack,
        Action onUpsReference,
        Action onCaribou,
        Action onLocusWebhook,
        Action onDsv,
        Action onP2PTrc,
        Action onDirectparcels,
        Action onNovaPoshtaInt,
        Action onFedexPoland,
        Action onCnJcex,
        Action onFarInternational,
        Action onIdexpress,
        Action onGangbao,
        Action onNeway,
        Action onPostnlInt3S,
        Action onRpxId,
        Action onDesignertransportWebhook,
        Action onGlsSloven,
        Action onParcelledIn,
        Action onGsiExpress,
        Action onConWay,
        Action onBrouwerTransport,
        Action onCpex,
        Action onIsraelPost,
        Action onDtdcIn,
        Action onPttPost,
        Action onXdeWebhook,
        Action onTolos,
        Action onGiaoHang,
        Action onGeodisEspace,
        Action onMagyarHu,
        Action onDoordashWebhook,
        Action onTikiId,
        Action onCjHkInternational,
        Action onStarTrackExpress,
        Action onHelthjem,
        Action onSfb2C,
        Action onFreightquote,
        Action onLandmarkGlobalReference,
        Action onParcel2Go,
        Action onDelnext,
        Action onRcl,
        Action onCgsExpress,
        Action onHkPost,
        Action onSapExpress,
        Action onParcelpostSg,
        Action onHermes,
        Action onIndSafeexpress,
        Action onTophatterexpress,
        Action onMglobal,
        Action onAveritt,
        Action onLeader,
        Action on_2Ebox,
        Action onSgSpeedpost,
        Action onDbschenkerSe,
        Action onIsrPostDomestic,
        Action onBestwayparcel,
        Action onAsendiaDe,
        Action onNightlineUk,
        Action onTaqbinSg,
        Action onTckExpress,
        Action onEndeavourDelivery,
        Action onNanjingwoyuan,
        Action onHeppnerFr,
        Action onEmpsCn,
        Action onFonsen,
        Action onPickrr,
        Action onApcOvernightConnum,
        Action onStarTrackNextFlight,
        Action onDajin,
        Action onUpsFreight,
        Action onPostaPlus,
        Action onCeva,
        Action onAnserx,
        Action onJsExpress,
        Action onPadtf,
        Action onUpsMailInnovations,
        Action onSypost,
        Action onAmazonShipMcf,
        Action onYusen,
        Action onBring,
        Action onSdaIt,
        Action onGba,
        Action onNeweggexpress,
        Action onSpeedcouriersGr,
        Action onForrun,
        Action onPickup,
        Action onEcms,
        Action onIntelipost,
        Action onFlashexpress,
        Action onCnSto,
        Action onSekoSftp,
        Action onHomeDeliverySolutions,
        Action onDpdHgry,
        Action onKerryttcVn,
        Action onJoyingBox,
        Action onTotalExpress,
        Action onZjsExpress,
        Action onStarken,
        Action onDemandship,
        Action onCnDpex,
        Action onAupostCn,
        Action onLogisters,
        Action onGoglobalpost,
        Action onGlsCz,
        Action onPaackWebhook,
        Action onGrabWebhook,
        Action onParcelpoint,
        Action onIcumulus,
        Action onDaiglobaltrack,
        Action onGlobalIparcel,
        Action onYurticiKargo,
        Action onCnPaypalPackage,
        Action onParcel2Post,
        Action onGlsIt,
        Action onPilLogistics,
        Action onHeppner,
        Action onGeneralOvernight,
        Action onHappy2Point,
        Action onChitchats,
        Action onSmooth,
        Action onCleLogistics,
        Action onFiege,
        Action onMxCargo,
        Action onZiingfinalmile,
        Action onDaytonFreight,
        Action onTcs,
        Action onAex,
        Action onHermesDe,
        Action onRoutificWebhook,
        Action onGlobavend,
        Action onCjLogistics,
        Action onPalletNetwork,
        Action onRafPh,
        Action onUkXdp,
        Action onPaperExpress,
        Action onLaPosteSuivi,
        Action onPaquetexpress,
        Action onLiefery,
        Action onStreckTransport,
        Action onPonyExpress,
        Action onAlwaysExpress,
        Action onGbsBroker,
        Action onCitylinkMy,
        Action onAlljoy,
        Action onYodel,
        Action onYodelDir,
        Action onStone3Pl,
        Action onParcelpalWebhook,
        Action onDhlEcomerceAsa,
        Action onSimplypost,
        Action onKyExpress,
        Action onShenzhen,
        Action onUsLasership,
        Action onUcExpre,
        Action onDidadi,
        Action onCjKr,
        Action onDbschenkerB2B,
        Action onMxe,
        Action onCaeDelivers,
        Action onPfcexpress,
        Action onWhistl,
        Action onWepost,
        Action onDhlParcelEs,
        Action onDdexpress,
        Action onAramexAu,
        Action onBneed,
        Action onHkTgx,
        Action onLatvijasPasts,
        Action onViaeurope,
        Action onCorreoUy,
        Action onChronopostFr,
        Action onJNet,
        Action on_6Ls,
        Action onBlrBelpost,
        Action onBirdsystem,
        Action onDobropost,
        Action onWahanaId,
        Action onWeaship,
        Action onSonictl,
        Action onKwt,
        Action onAfllogFtp,
        Action onSkynetWorldwide,
        Action onNovaPoshta,
        Action onSeino,
        Action onSzendex,
        Action onBpostInt,
        Action onDbschenkerSv,
        Action onAoDeutschland,
        Action onEuFleetSolutions,
        Action onPcfcorp,
        Action onLinkbridge,
        Action onPrimamulticipta,
        Action onCourex,
        Action onZajilExpress,
        Action onCollectco,
        Action onJtexpress,
        Action onFedexUk,
        Action onUship,
        Action onPixsell,
        Action onShiptor,
        Action onCdek,
        Action onVnmViettelpost,
        Action onCjCentury,
        Action onGso,
        Action onViwo,
        Action onSkybox,
        Action onKerrytj,
        Action onNtlogisticsVn,
        Action onSdhScm,
        Action onZinc,
        Action onDpeSouthAfrc,
        Action onCeskaCz,
        Action onAcsGr,
        Action onDealersend,
        Action onJocom,
        Action onCse,
        Action onTforceFinalmile,
        Action onShipGate,
        Action onShipter,
        Action onNationalSameday,
        Action onYunexpress,
        Action onCainiao,
        Action onDmsMatrix,
        Action onDirectlog,
        Action onAsendiaUs,
        Action on_3Jmslogistics,
        Action onLiccardiExpress,
        Action onSkyPostal,
        Action onCnwangtong,
        Action onPostnordLogisticsDk,
        Action onLogistika,
        Action onCeleritas,
        Action onPressiode,
        Action onShreeMaruti,
        Action onLogisticsworldwideHk,
        Action onEfex,
        Action onLotte,
        Action onLonestar,
        Action onAprisaexpress,
        Action onBelRs,
        Action onOsmWorldwide,
        Action onWestgateGl,
        Action onFastrack,
        Action onDtdExpr,
        Action onAlfatrex,
        Action onPromeddelivery,
        Action onThabitLogistics,
        Action onHctLogistics,
        Action onCarryFlap,
        Action onUsOldDominion,
        Action onAnicamBox,
        Action onWanbexpress,
        Action onAnPost,
        Action onDpdLocal,
        Action onStallionexpress,
        Action onRaiderex,
        Action onShopfans,
        Action onKyungdongParcel,
        Action onChampionLogistics,
        Action onPickuppSgp,
        Action onMorningExpress,
        Action onNacex,
        Action onThenileWebhook,
        Action onHolisol,
        Action onLbcexpressFtp,
        Action onKurasi,
        Action onUsfReddaway,
        Action onApg,
        Action onCnBoxc,
        Action onEcoscooting,
        Action onMainway,
        Action onPaperfly,
        Action onHoundexpress,
        Action onBoxBerry,
        Action onEpBox,
        Action onPlusLogUk,
        Action onFulfilla,
        Action onAse,
        Action onMailPlus,
        Action onXpoLogistics,
        Action onWndirect,
        Action onCloudwishAsia,
        Action onZeleris,
        Action onGioExpress,
        Action onOcsWorldwide,
        Action onArkLogistics,
        Action onAquiline,
        Action onPilotFreight,
        Action onQwintry,
        Action onDanskeFragt,
        Action onCarriers,
        Action onAirCanadaGlobal,
        Action onPresidentTrans,
        Action onStepforwardfs,
        Action onSkynetUk,
        Action onPittohio,
        Action onCorreosExpress,
        Action onRlUs,
        Action onDestiny,
        Action onUkYodel,
        Action onCometTech,
        Action onDhlParcelRu,
        Action onTntRefr,
        Action onShreeAnjaniCourier,
        Action onMikropakketBe,
        Action onEtsExpress,
        Action onColisPrive,
        Action onCnYunda,
        Action onAaaCooper,
        Action onRocketParcel,
        Action on_360Lion,
        Action onPandu,
        Action onProfessionalCouriers,
        Action onFlytexpress,
        Action onLogisticsworldwideMy,
        Action onCorreosDeEspana,
        Action onImx,
        Action onFourPxExpress,
        Action onXpressbees,
        Action onPickuppVnm,
        Action onStartrackExpress,
        Action onFrColissimo,
        Action onNacexSpainReference,
        Action onDhlSupplyChainAu,
        Action onEshipping,
        Action onShreetirupati,
        Action onHxExpress,
        Action onIndopaket,
        Action onCn17Post,
        Action onK1Express,
        Action onCjGls,
        Action onMysGdex,
        Action onNationex,
        Action onAnjun,
        Action onFargood,
        Action onSmgExpress,
        Action onRzyexpress,
        Action onSefl,
        Action onTntClickIt,
        Action onHdb,
        Action onHipshipper,
        Action onRpxlogistics,
        Action onKuehne,
        Action onItNexive,
        Action onPts,
        Action onSwissPostFtp,
        Action onFastrkServ,
        Action on_472,
        Action onUsYrc,
        Action onPostnlIntl3S,
        Action onElianPost,
        Action onCubyn,
        Action onSauSaudiPost,
        Action onAbxexpressMy,
        Action onHuahanExpress,
        Action onZesExpress,
        Action onZeptoExpress,
        Action onSkynetZa,
        Action onZeek2Door,
        Action onBlinklastmile,
        Action onPostaUkr,
        Action onChrobinson,
        Action onCnPost56,
        Action onCourantPlus,
        Action onScudexExpress,
        Action onShipentegra,
        Action onBTwoCEurope,
        Action onCope,
        Action onIndGati,
        Action onCnWishpost,
        Action onNacexEs,
        Action onTaqbinHk,
        Action onGlobaltranz,
        Action onHkd,
        Action onBjshomedelivery,
        Action onOmniva,
        Action onSutton,
        Action onPantherReference,
        Action onSfcservice,
        Action onLtl,
        Action onParknparcel,
        Action onSpringGds,
        Action onEcexpress,
        Action onInterparcelAu,
        Action onAgility,
        Action onXlExpress,
        Action onAderonline,
        Action onDirectcouriers,
        Action onPlanzer,
        Action onSending,
        Action onNinjavanWb,
        Action onNationwideMy,
        Action onSendit,
        Action onGbArrow,
        Action onIndGojavas,
        Action onKpost,
        Action onDhlFreight,
        Action onBluecare,
        Action onJindouyun,
        Action onTrackon,
        Action onGbTuffnells,
        Action onTrumpcard,
        Action onEtotal,
        Action onSfplusWebhook,
        Action onSekologistics,
        Action onHermes2MannHandling,
        Action onDpdLocalRef,
        Action onUds,
        Action onZaSpecialisedFreight,
        Action onThaKerry,
        Action onPrtIntSeur,
        Action onBraCorreios,
        Action onNzNzPost,
        Action onCnEquick,
        Action onMysEms,
        Action onGbNorsk,
        Action onEspMrw,
        Action onEspPacklink,
        Action onKangarooMy,
        Action onRpx,
        Action onXdpUkReference,
        Action onNinjavanMy,
        Action onAdicional,
        Action onRoadbull,
        Action onYakit,
        Action onMailamericas,
        Action onMikropakket,
        Action onDynalogic,
        Action onDhlEs,
        Action onDhlParcelNl,
        Action onDhlGlobalMailAsia,
        Action onDawnWing,
        Action onGenikiGr,
        Action onHermesworldUk,
        Action onAlphafast,
        Action onBuylogic,
        Action onEkart,
        Action onMexSenda,
        Action onSfcLogistics,
        Action onPostSerbia,
        Action onIndDelhivery,
        Action onDeDpdDelistrack,
        Action onRpd2Man,
        Action onCnSfExpress,
        Action onYanwen,
        Action onMysSkynet,
        Action onCorreosDeMexico,
        Action onCblLogistica,
        Action onMexEstafeta,
        Action onAuAustrianPost,
        Action onRincos,
        Action onNldDhl,
        Action onRussianPost,
        Action onCouriersPlease,
        Action onPostnordLogistics,
        Action onFedex,
        Action onDpeExpress,
        Action onDpd,
        Action onAdsone,
        Action onIdnJne,
        Action onThecourierguy,
        Action onCnexps,
        Action onPrtChronopost,
        Action onLandmarkGlobal,
        Action onItDhlEcommerce,
        Action onEspNacex,
        Action onPrtCtt,
        Action onBeKiala,
        Action onAsendiaUk,
        Action onGlobalTnt,
        Action onPosturIs,
        Action onEparcelKr,
        Action onInpostPaczkomaty,
        Action onItPosteItalia,
        Action onBeBpost,
        Action onPlPocztaPolska,
        Action onMysMysPost,
        Action onSgSgPost,
        Action onThaThailandPost,
        Action onLexship,
        Action onFastwayNz,
        Action onDhlAu,
        Action onCostmeticsnow,
        Action onPflogistics,
        Action onLoomisExpress,
        Action onGlsItaly,
        Action onLine,
        Action onGelExpress,
        Action onHuodull,
        Action onNinjavanSg,
        Action onJanio,
        Action onAoCourier,
        Action onBrtItSenderRef,
        Action onSailpost,
        Action onLalamove,
        Action onNewzealandCouriers,
        Action onEtomars,
        Action onVirtransport,
        Action onWizmo,
        Action onPalletways,
        Action onIDika,
        Action onCflLogistics,
        Action onGemworldwide,
        Action onGlobalExpress,
        Action onLogistyxTransgroup,
        Action onWestbankCourier,
        Action onArcoSpedizioni,
        Action onYdhExpress,
        Action onParcelinklogistics,
        Action onCndexpress,
        Action onNoxNightTimeExpress,
        Action onAeronet,
        Action onLtianexp,
        Action onIntegra2Ftp,
        Action onParcelone,
        Action onNoxNachtexpress,
        Action onCnChinaPostEms,
        Action onChukou1,
        Action onGlsSlov,
        Action onOrangeDs,
        Action onJoomLogis,
        Action onAusStartrack,
        Action onDhl,
        Action onGbApc,
        Action onBondscouriers,
        Action onJpnJapanPost,
        Action onUsps,
        Action onWinit,
        Action onArgOca,
        Action onTwTaiwanPost,
        Action onDmmNetwork,
        Action onTnt,
        Action onBhPosta,
        Action onSwePostnord,
        Action onCaCanadaPost,
        Action onWiseloads,
        Action onAsendiaHk,
        Action onNldGls,
        Action onMexRedpack,
        Action onJetShip,
        Action onDeDhlExpress,
        Action onNinjavanThai,
        Action onRabenGroup,
        Action onEspAsm,
        Action onHrvHrvatska,
        Action onGlobalEstes,
        Action onLtuLietuvos,
        Action onBelDhl,
        Action onAuAuPost,
        Action onSpeedexcourier,
        Action onFrColis,
        Action onAramex,
        Action onDpex,
        Action onMysAirpak,
        Action onCuckooexpress,
        Action onDpdPoland,
        Action onNldPostnl,
        Action onNimExpress,
        Action onQuantium,
        Action onSendle,
        Action onEspRedur,
        Action onMatkahuolto,
        Action onCpacket,
        Action onPosti,
        Action onHunterExpress,
        Action onChoirExp,
        Action onLegionExpress,
        Action onAustrianPostExpress,
        Action onGrupo,
        Action onPostaRo,
        Action onInterparcelUk,
        Action onGlobalAbf,
        Action onPostenNorge,
        Action onXpertDelivery,
        Action onDhlRefr,
        Action onDhlHk,
        Action onSkynetUae,
        Action onGojek,
        Action onYodelIntnl,
        Action onJanco,
        Action onYto,
        Action onWiseExpress,
        Action onJtexpressVn,
        Action onFedexIntlMlserv,
        Action onVamox,
        Action onAmsGrp,
        Action onDhlJp,
        Action onHrparcel,
        Action onGeswl,
        Action onBluestar,
        Action onCdekTr,
        Action onDescartes,
        Action onDeltecUk,
        Action onDtdcExpress,
        Action onTourline,
        Action onBhWorldwide,
        Action onOcs,
        Action onYingnuoLogistics,
        Action onUps,
        Action onToll,
        Action onPrtSeur,
        Action onDtdcAu,
        Action onThaDynamicLogistics,
        Action onUbiLogistics,
        Action onFedexCrossborder,
        Action onA1Post,
        Action onTazmanianFreight,
        Action onCjIntMy,
        Action onSaiaFreight,
        Action onSgQxpress,
        Action onNhansSolutions,
        Action onDpdFr,
        Action onCoordinadora,
        Action onAndreani,
        Action onDoora,
        Action onInterparcelNz,
        Action onPhlJamexpress,
        Action onBelBelgiumPost,
        Action onUsApc,
        Action onIdnPos,
        Action onFrMondial,
        Action onDeDhl,
        Action onHkRpx,
        Action onDhlPieceid,
        Action onVnpostEms,
        Action onRrdonnelley,
        Action onDpdDe,
        Action onDelcartIn,
        Action onImexglobalsolutions,
        Action onAcommerce,
        Action onEurodis,
        Action onCanpar,
        Action onGls,
        Action onIndEcom,
        Action onEspEnvialia,
        Action onDhlUk,
        Action onSmsaExpress,
        Action onTntFr,
        Action onDexI,
        Action onBudbeeWebhook,
        Action onCopaCourier,
        Action onVnmVietnamPost,
        Action onDpdHk,
        Action onTollNz,
        Action onEcho,
        Action onFedexFr,
        Action onBorderexpress,
        Action onMailplusJpn,
        Action onTntUkRefr,
        Action onKec,
        Action onDpdRo,
        Action onTntJp,
        Action onThCj,
        Action onEcCn,
        Action onFastwayUk,
        Action onFastwayUs,
        Action onGlsDe,
        Action onGlsEs,
        Action onGlsFr,
        Action onMondialBe,
        Action onSgtIt,
        Action onTntCn,
        Action onTntDe,
        Action onTntEs,
        Action onTntPl,
        Action onParcelforce,
        Action onSwissPost,
        Action onTollIpec,
        Action onAir21,
        Action onAirspeed,
        Action onBert,
        Action onBluedart,
        Action onCollectplus,
        Action onCourierplus,
        Action onCourierPost,
        Action onDhlGlobalMail,
        Action onDpdUk,
        Action onDeltecDe,
        Action onDeutscheDe,
        Action onDotzot,
        Action onEltaGr,
        Action onEmsCn,
        Action onEcargo,
        Action onEnsenda,
        Action onFercamIt,
        Action onFastwayZa,
        Action onFastwayAu,
        Action onFirstLogisitcs,
        Action onGeodis,
        Action onGlobegistics,
        Action onGreyhound,
        Action onJetshipMy,
        Action onLionParcel,
        Action onAeroflash,
        Action onOntrac,
        Action onSagawa,
        Action onSiodemka,
        Action onStartrack,
        Action onTntAu,
        Action onTntIt,
        Action onTransmission,
        Action onYamato,
        Action onDhlIt,
        Action onDhlAt,
        Action onLogisticsworldwideKr,
        Action onGlsSpain,
        Action onAmazonUkApi,
        Action onDpdFrReference,
        Action onDhlparcelUk,
        Action onMegasave,
        Action onQualitypost,
        Action onIdsLogistics,
        Action onJoyingbox,
        Action onPantherOrderNumber,
        Action onWatkinsShepard,
        Action onFasttrack,
        Action onUpExpress,
        Action onElogistica,
        Action onEcourier,
        Action onCjPhilippines,
        Action onSpeedex,
        Action onOrangeconnex,
        Action onTecor,
        Action onSaee,
        Action onGlsItalyFtp,
        Action onDelivere,
        Action onYycom,
        Action onAdicionalPt,
        Action onDksh,
        Action onNipponExpressFtp,
        Action onGols,
        Action onFujexp,
        Action onQtrack,
        Action onOmlogisticsApi,
        Action onGdpharm,
        Action onMisumiCn,
        Action onAirCanada,
        Action onCity56Webhook,
        Action onSagawaApi,
        Action onKedaex,
        Action onPgeonApi,
        Action onWeworldexpress,
        Action onJtLogistics,
        Action onTrusk,
        Action onViaxpress,
        Action onDhlSupplychainId,
        Action onZuelligpharmaSftp,
        Action onMeest,
        Action onTollPriority,
        Action onMothershipApi,
        Action onCapital,
        Action onEuropaketApi,
        Action onHfd,
        Action onTourlineReference,
        Action onGioEcourier,
        Action onCnLogistics,
        Action onPandion,
        Action onBpostApi,
        Action onPassportshipping,
        Action onPakajo,
        Action onDachser,
        Action onYusenSftp,
        Action onShyplite,
        Action onXyy,
        Action onMwd,
        Action onFaxecargo,
        Action onMazet,
        Action onFirstLogisticsApi,
        Action onSprintPack,
        Action onHermesDeFtp,
        Action onConcise,
        Action onKerryExpressTwApi,
        Action onEwe,
        Action onFastdespatch,
        Action onAbcustomSftp,
        Action onChazki,
        Action onShippie,
        Action onGeodisApi,
        Action onNaqelExpress,
        Action onPapaWebhook,
        Action onForwardair,
        Action onDialogoLogisticaApi,
        Action onLalamoveApi,
        Action onTomydoor,
        Action onKronosWebhook,
        Action onJtcargo,
        Action onTCat,
        Action onConciseWebhook,
        Action onTeleportWebhook,
        Action onCustomcoApi,
        Action onSpxTh,
        Action onBolloreLogistics,
        Action onClicklinkSftp,
        Action onM3Logistics,
        Action onVnpostApi,
        Action onAxlehireFtp,
        Action onShadowfax,
        Action onMyhermesUkApi,
        Action onDaiichi,
        Action onMensajerosurbanosApi,
        Action onPolarspeed,
        Action onIdexpressId,
        Action onPayo,
        Action onWhistlSftp,
        Action onIntexDe,
        Action onTrans2U,
        Action onProductcaregroupSftp,
        Action onBigsmart,
        Action onExpeditorsApiRef,
        Action onAitworldwideApi,
        Action onWorldcourier,
        Action onQuiqup,
        Action onAgedissSftp,
        Action onAndreaniApi,
        Action onCrlexpress,
        Action onSmartcat,
        Action onCrossflight,
        Action onProcarrier,
        Action onDhlReferenceApi,
        Action onSeinoApi,
        Action onWspexpress,
        Action onKronos,
        Action onTotalExpressApi,
        Action onParcll,
        Action onXpedigo,
        Action onStarTrackWebhook,
        Action onGpost,
        Action onUcs,
        Action onDmfgroup,
        Action onCoordinadoraApi,
        Action onMarken,
        Action onNtl,
        Action onRedjepakketje,
        Action onAlliedExpressFtp,
        Action onMondialrelayEs,
        Action onNaekoFtp,
        Action onMhi,
        Action onShippify,
        Action onMalcaAmitApi,
        Action onJtexpressSgApi,
        Action onDachserWeb,
        Action onFlightlg,
        Action onCago,
        Action onCom1Express,
        Action onTonamiFtp,
        Action onPackfleet,
        Action onPurolatorInternational,
        Action onWineshippingWebhook,
        Action onDhlEsSftp,
        Action onPchomeApi,
        Action onCeskapostaApi,
        Action onGorush,
        Action onHomerunner,
        Action onAmazonOrder,
        Action onEfwnowApi,
        Action onCblLogisticaApi,
        Action onNimbuspost,
        Action onLogwinLogistics,
        Action onNowlogApi,
        Action onDpdNl,
        Action onGodependable,
        Action onEsdex,
        Action onLogisystemsSftp,
        Action onExpeditors,
        Action onSntglobalApi,
        Action onShipx,
        Action onQintlApi,
        Action onPacks,
        Action onPostnlInternational,
        Action onAmazonEmailPush,
        Action onDhlApi,
        Action onSpx,
        Action onAxlehire,
        Action onIcscourier,
        Action onDialogoLogistica,
        Action onShunbangExpress,
        Action onTcsApi,
        Action onSfExpressCn,
        Action onPacketa,
        Action onSicTeliway,
        Action onMondialrelayFr,
        Action onIntimeFtp,
        Action onJdExpress,
        Action onFastbox,
        Action onPatheon,
        Action onIndiaPost,
        Action onTipsaRef,
        Action onEcofreight,
        Action onVox,
        Action onDirectfreightAuRef,
        Action onBesttransportSftp,
        Action onAustraliaPostApi,
        Action onFragilepakSftp,
        Action onFlipxp,
        Action onValueWebhook,
        Action onDaeshin,
        Action onSherpa,
        Action onMwdApi,
        Action onSmartkargo,
        Action onDnjExpress,
        Action onGopeople,
        Action onMysendleApi,
        Action onAramexApi,
        Action onPidge,
        Action onThaiparcels,
        Action onPantherReferenceApi,
        Action onPostaplus,
        Action onBuffalo,
        Action onUEnvios,
        Action onEliteCo,
        Action onRocheInternalSftp,
        Action onDbschenkerIceland,
        Action onTntFrReference,
        Action onNewgisticsapi,
        Action onGlovo,
        Action onGwlogisApi,
        Action onSpreetailApi,
        Action onMoova,
        Action onPlycongroup,
        Action onUspsWebhook,
        Action onReimaginedelivery,
        Action onEdfFtp,
        Action onDao365,
        Action onBiocairFtp,
        Action onRansaWebhook,
        Action onShipxpres,
        Action onCourantPlusApi,
        Action onShipa,
        Action onHomelogistics,
        Action onDx,
        Action onPosteItalianePaccocelere,
        Action onTollWebhook,
        Action onLctbrApi,
        Action onDxFreight,
        Action onDhlSftp,
        Action onShiprocket,
        Action onUberWebhook,
        Action onStatovernight,
        Action onBurd,
        Action onFastship,
        Action onIbventureWebhook,
        Action onGatiKweApi,
        Action onCryopdpFtp,
        Action onHubbed,
        Action onTipsaApi,
        Action onAraskargo,
        Action onThijsNl,
        Action onAtshealthcareReference,
        Action on_99Minutos,
        Action onHellenicPost,
        Action onHsmGlobal,
        Action onMnx,
        Action onNmtransfer,
        Action onLogysto,
        Action onIndiaPostInt,
        Action onAmazonFbaSwishipIn,
        Action onSrtTransport,
        Action onBomi,
        Action onDeliverrSftp,
        Action onHsdexpress,
        Action onSimpletireWebhook,
        Action onHunterExpressSftp,
        Action onUpsApi,
        Action onWooyoungLogisticsSftp,
        Action onPhseApi,
        Action onWishEmailPush,
        Action onNorthline,
        Action onMedafrica,
        Action onDpdAtSftp,
        Action onAnteraja,
        Action onDhlGlobalForwardingApi,
        Action onLbcexpressApi,
        Action onSimsglobal,
        Action onCdldelivers,
        Action onTyp,
        Action onTestingCourierWebhook,
        Action onPandagoApi,
        Action onRoyalMailFtp,
        Action onThunderexpress,
        Action onSecretlabWebhook,
        Action onSetel,
        Action onJdWorldwide,
        Action onDpdRuApi,
        Action onArgentsWebhook,
        Action onPostone,
        Action onTusklogistics,
        Action onRhenusUkApi,
        Action onTaqbinSgApi,
        Action onInntralogSftp,
        Action onDayross,
        Action onCorreosexpressApi,
        Action onInternationalSeurApi,
        Action onYodelApi,
        Action onHeroexpress,
        Action onDhlSupplychainIn,
        Action onUrgentCargus,
        Action onFrontdoorcorp,
        Action onJtexpressPh,
        Action onParcelstarsWebhook,
        Action onDpdSkSftp,
        Action onMovianto,
        Action onOzepartsShipping,
        Action onKargomkolay,
        Action onTrunkrs,
        Action onOmnirpsWebhook,
        Action onChilexpress,
        Action onTestingCourier,
        Action onJneApi,
        Action onBjshomedeliveryFtp,
        Action onDexpressWebhook,
        Action onUspsApi,
        Action onTransvirtual,
        Action onSolisticaApi,
        Action onChienventureWebhook,
        Action onDpdUkSftp,
        Action onInpostUk,
        Action onJavit,
        Action onZtoDomestic,
        Action onDhlGtApi,
        Action onCevaTracking,
        Action onKomonExpress,
        Action onEastwestcourierFtp,
        Action onDanniao,
        Action onSpectran,
        Action onDeliverIt,
        Action onRelaiscolis,
        Action onGlsSpainApi,
        Action onPostplus,
        Action onAirterra,
        Action onGioEcourierApi,
        Action onDpdChSftp,
        Action onFedexApi,
        Action onIntersmarttrans,
        Action onHermesUkSftp,
        Action onExelotFtp,
        Action onDhlPaApi,
        Action onVirtransportSftp,
        Action onWorldnet,
        Action onInstaboxWebhook,
        Action onKng,
        Action onFlashexpressWebhook,
        Action onMagyarPostaApi,
        Action onWeshipApi,
        Action onOhiWebhook,
        Action onMudita,
        Action onBluedartApi,
        Action onTCatApi,
        Action onAds,
        Action onHermesIt,
        Action onFitzmarkApi,
        Action onPostiApi,
        Action onSmsaExpressWebhook,
        Action onTamergroupWebhook,
        Action onLivrapide,
        Action onNipponExpress,
        Action onBettertrucks,
        Action onFan,
        Action onPbUspsflatsFtp,
        Action onParcelright,
        Action onIthinklogistics,
        Action onKerryExpressThWebhook,
        Action onEcoutier,
        Action onShowl,
        Action onBrtItApi,
        Action onRixonhkApi,
        Action onDbschenkerApi,
        Action onIlyanglogis,
        Action onMailBoxEtc,
        Action onWeship,
        Action onDhlGlobalMailApi,
        Action onActivos24Api,
        Action onAtshealthcare,
        Action onLuwjistik,
        Action onGwWorld,
        Action onFairsendenApi,
        Action onServipWebhook,
        Action onSwiship,
        Action onTanet,
        Action onHotsinCargo,
        Action onDirex,
        Action onHuantong,
        Action onImileApi,
        Action onAuexpress,
        Action onNytlogistics,
        Action onDsvReference,
        Action onNovofarmaWebhook,
        Action onAitworldwideSftp,
        Action onShopolive,
        Action onFnfZa,
        Action onDhlEcommerceGc,
        Action onFetchr,
        Action onStarlinksApi,
        Action onYyexpress,
        Action onServientrega,
        Action onHanjin,
        Action onSpanishSeurFtp,
        Action onDxB2BConnum,
        Action onHelthjemApi,
        Action onInexpost,
        Action onA2BBa,
        Action onRhenusGroup,
        Action onSberlogisticsRu,
        Action onMalcaAmit,
        Action onPpl,
        Action onOsmWorldwideSftp,
        Action onAcilogistix,
        Action onOptimacourier,
        Action onNovaPoshtaApi,
        Action onLoggi,
        Action onYifan,
        Action onMydynalogic,
        Action onMorninglobal,
        Action onConciseApi,
        Action onFxtran,
        Action onDeliveryourparcelZa,
        Action onUparcel,
        Action onMobiBr,
        Action onLoginextWebhook,
        Action onEms,
        Action onSpeedy,
        Action onZoomRed,
        Action onNavlungo,
        Action onCastleparcels,
        Action onWeee,
        Action onPackaly,
        Action onYunhuipost,
        Action onYouparcel,
        Action onLeman,
        Action onMoovin,
        Action onUrbIt,
        Action onMultientregapanama,
        Action onJusdasr,
        Action onDiscountpost,
        Action onRhenusUk,
        Action onSwishipJp,
        Action onGlsUs,
        Action onSmtl,
        Action onEmega,
        Action onExpressoneSv,
        Action onHepsijet,
        Action onWelivery,
        Action onBringer,
        Action onEasyroutes,
        Action onMrw,
        Action onRpm,
        Action onDpdPrt,
        Action onGlsRomania,
        Action onLmparcel,
        Action onGtagsm,
        Action onDomino,
        Action onEshipper,
        Action onTranspak,
        Action onXindus,
        Action onAoyue,
        Action onEasyparcel,
        Action onExpressone,
        Action onSendeoKargo,
        Action onSpeedaf,
        Action onEtower,
        Action onGcx,
        Action onNinjavanVn,
        Action onAllegro,
        Action onJumppoint,
        Action onShipglobalUs,
        Action onKinisi,
        Action onOakh,
        Action onAwest,
        Action onBarsan,
        Action onEnergologistic,
        Action onMadrooex,
        Action onGobolt,
        Action onSwissUniversalExpress,
        Action onIordirect,
        Action onXmszm,
        Action onGlsHun,
        Action onSendy,
        Action onBraunsexpress,
        Action onGrandslamexpress,
        Action onXgs,
        Action onOtschile,
        Action onPackUp,
        Action onParcelstars,
        Action onTeamexpressllc,
        Action onAsyadexpress,
        Action onTdn,
        Action onEarlybird,
        Action onCacesa,
        Action onParceljet,
        Action onMngKargo,
        Action onSuperpackline,
        Action onSpeedx,
        Action onVesyl,
        Action onSkyking,
        Action onDirmensajeria,
        Action onNetlogixgroup,
        Action onZyou,
        Action onJawar,
        Action onAgsystems,
        Action onGps,
        Action onPttKargo,
        Action onMaergo,
        Action onArihantcourier,
        Action onVtfe,
        Action onYunant,
        Action onUrbify,
        Action onPackMan,
        Action onLiefergrun,
        Action onObibox,
        Action onPaikeda,
        Action onScotty,
        Action onIntelcomCa,
        Action onSwe,
        Action onAsendia,
        Action onDpdAt,
        Action onRelay,
        Action onAta,
        Action onSkyexpressInternational,
        Action onSuratKargo,
        Action onSglink,
        Action onFleetopticsinc,
        Action onShopline,
        Action onPiggyship,
        Action onLogoix,
        Action onKolayGelsin,
        Action onAssociatedCouriers,
        Action onUpsChecker,
        Action onWineshipping,
        Action onSpedisci,
        Action onFourkites,
        Action onEtonas,
        Action onFinmile,
        Action onUniuni,
        Action onRodonaves,
        Action onInpostIt,
        Action onTforceFreight,
        Action onRichmom,
        Action onFranco,
        Action onEcparcel,
        Action onFedexChina,
        Action onGofoExpress,
        Action onShipbob,
        Action onJerseypostAtlas,
        Action onCoretrails,
        Action onRhenusItaly,
        Action onJadlog,
        Action onJitsu,
        Action onYanwenExpress,
        Action onDashlink,
        Action onSeinoSuperExpress,
        Action onFloship,
        Action onMetroscg,
        Action onSendparcel,
        Action onP2P,
        Action onCnExpress,
        Action onCirrotrack,
        Action onLandLogistics,
        Action onVeho,
        Action onMedline,
        Action onVdtrack,
        Action onSinoScm,
        Action on_3PeExpress,
        Action onSwiftx,
        Action onSfydexpress,
        Action onToptrans,
        Action onOther,
        Action<string> otherwise)
    {
        if (this == DpdRu) onDpdRu();
        else if (this == BgBulgarianPost) onBgBulgarianPost();
        else if (this == KrKoreaPost) onKrKoreaPost();
        else if (this == ZaCourierit) onZaCourierit();
        else if (this == FrExapaq) onFrExapaq();
        else if (this == AreEmiratesPost) onAreEmiratesPost();
        else if (this == Gac) onGac();
        else if (this == Geis) onGeis();
        else if (this == SfEx) onSfEx();
        else if (this == Pago) onPago();
        else if (this == Myhermes) onMyhermes();
        else if (this == DiamondEurogistics) onDiamondEurogistics();
        else if (this == CorporatecouriersWebhook) onCorporatecouriersWebhook();
        else if (this == Bond) onBond();
        else if (this == Omniparcel) onOmniparcel();
        else if (this == SkPosta) onSkPosta();
        else if (this == Purolator) onPurolator();
        else if (this == FetchrWebhook) onFetchrWebhook();
        else if (this == Thedeliverygroup) onThedeliverygroup();
        else if (this == CelloSquare) onCelloSquare();
        else if (this == Tarrive) onTarrive();
        else if (this == Collivery) onCollivery();
        else if (this == Mainfreight) onMainfreight();
        else if (this == IndFirstflight) onIndFirstflight();
        else if (this == Acsworldwide) onAcsworldwide();
        else if (this == Amstan) onAmstan();
        else if (this == Okayparcel) onOkayparcel();
        else if (this == EnvialiaReference) onEnvialiaReference();
        else if (this == SeurEs) onSeurEs();
        else if (this == Continental) onContinental();
        else if (this == Fdsexpress) onFdsexpress();
        else if (this == AmazonFbaSwiship) onAmazonFbaSwiship();
        else if (this == Wyngs) onWyngs();
        else if (this == DhlActiveTracing) onDhlActiveTracing();
        else if (this == Zyllem) onZyllem();
        else if (this == Ruston) onRuston();
        else if (this == Xpost) onXpost();
        else if (this == CorreosEs) onCorreosEs();
        else if (this == DhlFr) onDhlFr();
        else if (this == PanAsia) onPanAsia();
        else if (this == BrtIt) onBrtIt();
        else if (this == SreKorea) onSreKorea();
        else if (this == Speedee) onSpeedee();
        else if (this == TntUk) onTntUk();
        else if (this == Venipak) onVenipak();
        else if (this == Shreenandancourier) onShreenandancourier();
        else if (this == Croshot) onCroshot();
        else if (this == NipostNg) onNipostNg();
        else if (this == EpstGlbl) onEpstGlbl();
        else if (this == Newgistics) onNewgistics();
        else if (this == PostSlovenia) onPostSlovenia();
        else if (this == JerseyPost) onJerseyPost();
        else if (this == Bombinoexp) onBombinoexp();
        else if (this == Wmg) onWmg();
        else if (this == XqExpress) onXqExpress();
        else if (this == Furdeco) onFurdeco();
        else if (this == LhtExpress) onLhtExpress();
        else if (this == SouthAfricanPostOffice) onSouthAfricanPostOffice();
        else if (this == Spoton) onSpoton();
        else if (this == Dimerco) onDimerco();
        else if (this == CyprusPostCyp) onCyprusPostCyp();
        else if (this == Abcustom) onAbcustom();
        else if (this == IndDelivree) onIndDelivree();
        else if (this == CnBestexpress) onCnBestexpress();
        else if (this == DxSftp) onDxSftp();
        else if (this == PickuppMys) onPickuppMys();
        else if (this == Fmx) onFmx();
        else if (this == Hellmann) onHellmann();
        else if (this == ShipItAsia) onShipItAsia();
        else if (this == KerryEcommerce) onKerryEcommerce();
        else if (this == Freterapido) onFreterapido();
        else if (this == PitneyBowes) onPitneyBowes();
        else if (this == XpressenDk) onXpressenDk();
        else if (this == SeurSpApi) onSeurSpApi();
        else if (this == Deliveryontime) onDeliveryontime();
        else if (this == Jinsung) onJinsung();
        else if (this == TransKargo) onTransKargo();
        else if (this == SwishipDe) onSwishipDe();
        else if (this == IvoyWebhook) onIvoyWebhook();
        else if (this == AirmeeWebhook) onAirmeeWebhook();
        else if (this == DhlBenelux) onDhlBenelux();
        else if (this == Firstmile) onFirstmile();
        else if (this == FastwayIr) onFastwayIr();
        else if (this == HhExp) onHhExp();
        else if (this == MysMypostOnline) onMysMypostOnline();
        else if (this == TntNl) onTntNl();
        else if (this == Tipsa) onTipsa();
        else if (this == TaqbinMy) onTaqbinMy();
        else if (this == Kgmhub) onKgmhub();
        else if (this == Intexpress) onIntexpress();
        else if (this == OverseExp) onOverseExp();
        else if (this == Oneclick) onOneclick();
        else if (this == RoadrunnerFreight) onRoadrunnerFreight();
        else if (this == GlsCrotia) onGlsCrotia();
        else if (this == MrwFtp) onMrwFtp();
        else if (this == Bluex) onBluex();
        else if (this == Dylt) onDylt();
        else if (this == DpdIr) onDpdIr();
        else if (this == SinGlbl) onSinGlbl();
        else if (this == TuffnellsReference) onTuffnellsReference();
        else if (this == Cjpacket) onCjpacket();
        else if (this == Milkman) onMilkman();
        else if (this == Asigna) onAsigna();
        else if (this == Oneworldexpress) onOneworldexpress();
        else if (this == RoyalMail) onRoyalMail();
        else if (this == ViaExpress) onViaExpress();
        else if (this == Tigfreight) onTigfreight();
        else if (this == ZtoExpress) onZtoExpress();
        else if (this == TwoGo) onTwoGo();
        else if (this == Iml) onIml();
        else if (this == IntelValley) onIntelValley();
        else if (this == Efs) onEfs();
        else if (this == UkUkMail) onUkUkMail();
        else if (this == Ram) onRam();
        else if (this == Alliedexpress) onAlliedexpress();
        else if (this == ApcOvernight) onApcOvernight();
        else if (this == Shippit) onShippit();
        else if (this == Tfm) onTfm();
        else if (this == MXpress) onMXpress();
        else if (this == HdbBox) onHdbBox();
        else if (this == ClevyLinks) onClevyLinks();
        else if (this == Ibeone) onIbeone();
        else if (this == FiegeNl) onFiegeNl();
        else if (this == KweGlobal) onKweGlobal();
        else if (this == CtcExpress) onCtcExpress();
        else if (this == Amazon) onAmazon();
        else if (this == MoreLink) onMoreLink();
        else if (this == Jx) onJx();
        else if (this == EasyMail) onEasyMail();
        else if (this == Aduiepyle) onAduiepyle();
        else if (this == GbPanther) onGbPanther();
        else if (this == Expresssale) onExpresssale();
        else if (this == SgDetrack) onSgDetrack();
        else if (this == TrunkrsWebhook) onTrunkrsWebhook();
        else if (this == Matdespatch) onMatdespatch();
        else if (this == Dicom) onDicom();
        else if (this == Mbw) onMbw();
        else if (this == KhmCambodiaPost) onKhmCambodiaPost();
        else if (this == Sinotrans) onSinotrans();
        else if (this == BrtItParcelid) onBrtItParcelid();
        else if (this == DhlSupplyChain) onDhlSupplyChain();
        else if (this == DhlPl) onDhlPl();
        else if (this == Topyou) onTopyou();
        else if (this == Palexpress) onPalexpress();
        else if (this == DhlSg) onDhlSg();
        else if (this == CnWedo) onCnWedo();
        else if (this == Fulfillme) onFulfillme();
        else if (this == DpdDelistrack) onDpdDelistrack();
        else if (this == UpsReference) onUpsReference();
        else if (this == Caribou) onCaribou();
        else if (this == LocusWebhook) onLocusWebhook();
        else if (this == Dsv) onDsv();
        else if (this == P2PTrc) onP2PTrc();
        else if (this == Directparcels) onDirectparcels();
        else if (this == NovaPoshtaInt) onNovaPoshtaInt();
        else if (this == FedexPoland) onFedexPoland();
        else if (this == CnJcex) onCnJcex();
        else if (this == FarInternational) onFarInternational();
        else if (this == Idexpress) onIdexpress();
        else if (this == Gangbao) onGangbao();
        else if (this == Neway) onNeway();
        else if (this == PostnlInt3S) onPostnlInt3S();
        else if (this == RpxId) onRpxId();
        else if (this == DesignertransportWebhook) onDesignertransportWebhook();
        else if (this == GlsSloven) onGlsSloven();
        else if (this == ParcelledIn) onParcelledIn();
        else if (this == GsiExpress) onGsiExpress();
        else if (this == ConWay) onConWay();
        else if (this == BrouwerTransport) onBrouwerTransport();
        else if (this == Cpex) onCpex();
        else if (this == IsraelPost) onIsraelPost();
        else if (this == DtdcIn) onDtdcIn();
        else if (this == PttPost) onPttPost();
        else if (this == XdeWebhook) onXdeWebhook();
        else if (this == Tolos) onTolos();
        else if (this == GiaoHang) onGiaoHang();
        else if (this == GeodisEspace) onGeodisEspace();
        else if (this == MagyarHu) onMagyarHu();
        else if (this == DoordashWebhook) onDoordashWebhook();
        else if (this == TikiId) onTikiId();
        else if (this == CjHkInternational) onCjHkInternational();
        else if (this == StarTrackExpress) onStarTrackExpress();
        else if (this == Helthjem) onHelthjem();
        else if (this == Sfb2C) onSfb2C();
        else if (this == Freightquote) onFreightquote();
        else if (this == LandmarkGlobalReference) onLandmarkGlobalReference();
        else if (this == Parcel2Go) onParcel2Go();
        else if (this == Delnext) onDelnext();
        else if (this == Rcl) onRcl();
        else if (this == CgsExpress) onCgsExpress();
        else if (this == HkPost) onHkPost();
        else if (this == SapExpress) onSapExpress();
        else if (this == ParcelpostSg) onParcelpostSg();
        else if (this == Hermes) onHermes();
        else if (this == IndSafeexpress) onIndSafeexpress();
        else if (this == Tophatterexpress) onTophatterexpress();
        else if (this == Mglobal) onMglobal();
        else if (this == Averitt) onAveritt();
        else if (this == Leader) onLeader();
        else if (this == _2Ebox) on_2Ebox();
        else if (this == SgSpeedpost) onSgSpeedpost();
        else if (this == DbschenkerSe) onDbschenkerSe();
        else if (this == IsrPostDomestic) onIsrPostDomestic();
        else if (this == Bestwayparcel) onBestwayparcel();
        else if (this == AsendiaDe) onAsendiaDe();
        else if (this == NightlineUk) onNightlineUk();
        else if (this == TaqbinSg) onTaqbinSg();
        else if (this == TckExpress) onTckExpress();
        else if (this == EndeavourDelivery) onEndeavourDelivery();
        else if (this == Nanjingwoyuan) onNanjingwoyuan();
        else if (this == HeppnerFr) onHeppnerFr();
        else if (this == EmpsCn) onEmpsCn();
        else if (this == Fonsen) onFonsen();
        else if (this == Pickrr) onPickrr();
        else if (this == ApcOvernightConnum) onApcOvernightConnum();
        else if (this == StarTrackNextFlight) onStarTrackNextFlight();
        else if (this == Dajin) onDajin();
        else if (this == UpsFreight) onUpsFreight();
        else if (this == PostaPlus) onPostaPlus();
        else if (this == Ceva) onCeva();
        else if (this == Anserx) onAnserx();
        else if (this == JsExpress) onJsExpress();
        else if (this == Padtf) onPadtf();
        else if (this == UpsMailInnovations) onUpsMailInnovations();
        else if (this == Sypost) onSypost();
        else if (this == AmazonShipMcf) onAmazonShipMcf();
        else if (this == Yusen) onYusen();
        else if (this == Bring) onBring();
        else if (this == SdaIt) onSdaIt();
        else if (this == Gba) onGba();
        else if (this == Neweggexpress) onNeweggexpress();
        else if (this == SpeedcouriersGr) onSpeedcouriersGr();
        else if (this == Forrun) onForrun();
        else if (this == Pickup) onPickup();
        else if (this == Ecms) onEcms();
        else if (this == Intelipost) onIntelipost();
        else if (this == Flashexpress) onFlashexpress();
        else if (this == CnSto) onCnSto();
        else if (this == SekoSftp) onSekoSftp();
        else if (this == HomeDeliverySolutions) onHomeDeliverySolutions();
        else if (this == DpdHgry) onDpdHgry();
        else if (this == KerryttcVn) onKerryttcVn();
        else if (this == JoyingBox) onJoyingBox();
        else if (this == TotalExpress) onTotalExpress();
        else if (this == ZjsExpress) onZjsExpress();
        else if (this == Starken) onStarken();
        else if (this == Demandship) onDemandship();
        else if (this == CnDpex) onCnDpex();
        else if (this == AupostCn) onAupostCn();
        else if (this == Logisters) onLogisters();
        else if (this == Goglobalpost) onGoglobalpost();
        else if (this == GlsCz) onGlsCz();
        else if (this == PaackWebhook) onPaackWebhook();
        else if (this == GrabWebhook) onGrabWebhook();
        else if (this == Parcelpoint) onParcelpoint();
        else if (this == Icumulus) onIcumulus();
        else if (this == Daiglobaltrack) onDaiglobaltrack();
        else if (this == GlobalIparcel) onGlobalIparcel();
        else if (this == YurticiKargo) onYurticiKargo();
        else if (this == CnPaypalPackage) onCnPaypalPackage();
        else if (this == Parcel2Post) onParcel2Post();
        else if (this == GlsIt) onGlsIt();
        else if (this == PilLogistics) onPilLogistics();
        else if (this == Heppner) onHeppner();
        else if (this == GeneralOvernight) onGeneralOvernight();
        else if (this == Happy2Point) onHappy2Point();
        else if (this == Chitchats) onChitchats();
        else if (this == Smooth) onSmooth();
        else if (this == CleLogistics) onCleLogistics();
        else if (this == Fiege) onFiege();
        else if (this == MxCargo) onMxCargo();
        else if (this == Ziingfinalmile) onZiingfinalmile();
        else if (this == DaytonFreight) onDaytonFreight();
        else if (this == Tcs) onTcs();
        else if (this == Aex) onAex();
        else if (this == HermesDe) onHermesDe();
        else if (this == RoutificWebhook) onRoutificWebhook();
        else if (this == Globavend) onGlobavend();
        else if (this == CjLogistics) onCjLogistics();
        else if (this == PalletNetwork) onPalletNetwork();
        else if (this == RafPh) onRafPh();
        else if (this == UkXdp) onUkXdp();
        else if (this == PaperExpress) onPaperExpress();
        else if (this == LaPosteSuivi) onLaPosteSuivi();
        else if (this == Paquetexpress) onPaquetexpress();
        else if (this == Liefery) onLiefery();
        else if (this == StreckTransport) onStreckTransport();
        else if (this == PonyExpress) onPonyExpress();
        else if (this == AlwaysExpress) onAlwaysExpress();
        else if (this == GbsBroker) onGbsBroker();
        else if (this == CitylinkMy) onCitylinkMy();
        else if (this == Alljoy) onAlljoy();
        else if (this == Yodel) onYodel();
        else if (this == YodelDir) onYodelDir();
        else if (this == Stone3Pl) onStone3Pl();
        else if (this == ParcelpalWebhook) onParcelpalWebhook();
        else if (this == DhlEcomerceAsa) onDhlEcomerceAsa();
        else if (this == Simplypost) onSimplypost();
        else if (this == KyExpress) onKyExpress();
        else if (this == Shenzhen) onShenzhen();
        else if (this == UsLasership) onUsLasership();
        else if (this == UcExpre) onUcExpre();
        else if (this == Didadi) onDidadi();
        else if (this == CjKr) onCjKr();
        else if (this == DbschenkerB2B) onDbschenkerB2B();
        else if (this == Mxe) onMxe();
        else if (this == CaeDelivers) onCaeDelivers();
        else if (this == Pfcexpress) onPfcexpress();
        else if (this == Whistl) onWhistl();
        else if (this == Wepost) onWepost();
        else if (this == DhlParcelEs) onDhlParcelEs();
        else if (this == Ddexpress) onDdexpress();
        else if (this == AramexAu) onAramexAu();
        else if (this == Bneed) onBneed();
        else if (this == HkTgx) onHkTgx();
        else if (this == LatvijasPasts) onLatvijasPasts();
        else if (this == Viaeurope) onViaeurope();
        else if (this == CorreoUy) onCorreoUy();
        else if (this == ChronopostFr) onChronopostFr();
        else if (this == JNet) onJNet();
        else if (this == _6Ls) on_6Ls();
        else if (this == BlrBelpost) onBlrBelpost();
        else if (this == Birdsystem) onBirdsystem();
        else if (this == Dobropost) onDobropost();
        else if (this == WahanaId) onWahanaId();
        else if (this == Weaship) onWeaship();
        else if (this == Sonictl) onSonictl();
        else if (this == Kwt) onKwt();
        else if (this == AfllogFtp) onAfllogFtp();
        else if (this == SkynetWorldwide) onSkynetWorldwide();
        else if (this == NovaPoshta) onNovaPoshta();
        else if (this == Seino) onSeino();
        else if (this == Szendex) onSzendex();
        else if (this == BpostInt) onBpostInt();
        else if (this == DbschenkerSv) onDbschenkerSv();
        else if (this == AoDeutschland) onAoDeutschland();
        else if (this == EuFleetSolutions) onEuFleetSolutions();
        else if (this == Pcfcorp) onPcfcorp();
        else if (this == Linkbridge) onLinkbridge();
        else if (this == Primamulticipta) onPrimamulticipta();
        else if (this == Courex) onCourex();
        else if (this == ZajilExpress) onZajilExpress();
        else if (this == Collectco) onCollectco();
        else if (this == Jtexpress) onJtexpress();
        else if (this == FedexUk) onFedexUk();
        else if (this == Uship) onUship();
        else if (this == Pixsell) onPixsell();
        else if (this == Shiptor) onShiptor();
        else if (this == Cdek) onCdek();
        else if (this == VnmViettelpost) onVnmViettelpost();
        else if (this == CjCentury) onCjCentury();
        else if (this == Gso) onGso();
        else if (this == Viwo) onViwo();
        else if (this == Skybox) onSkybox();
        else if (this == Kerrytj) onKerrytj();
        else if (this == NtlogisticsVn) onNtlogisticsVn();
        else if (this == SdhScm) onSdhScm();
        else if (this == Zinc) onZinc();
        else if (this == DpeSouthAfrc) onDpeSouthAfrc();
        else if (this == CeskaCz) onCeskaCz();
        else if (this == AcsGr) onAcsGr();
        else if (this == Dealersend) onDealersend();
        else if (this == Jocom) onJocom();
        else if (this == Cse) onCse();
        else if (this == TforceFinalmile) onTforceFinalmile();
        else if (this == ShipGate) onShipGate();
        else if (this == Shipter) onShipter();
        else if (this == NationalSameday) onNationalSameday();
        else if (this == Yunexpress) onYunexpress();
        else if (this == Cainiao) onCainiao();
        else if (this == DmsMatrix) onDmsMatrix();
        else if (this == Directlog) onDirectlog();
        else if (this == AsendiaUs) onAsendiaUs();
        else if (this == _3Jmslogistics) on_3Jmslogistics();
        else if (this == LiccardiExpress) onLiccardiExpress();
        else if (this == SkyPostal) onSkyPostal();
        else if (this == Cnwangtong) onCnwangtong();
        else if (this == PostnordLogisticsDk) onPostnordLogisticsDk();
        else if (this == Logistika) onLogistika();
        else if (this == Celeritas) onCeleritas();
        else if (this == Pressiode) onPressiode();
        else if (this == ShreeMaruti) onShreeMaruti();
        else if (this == LogisticsworldwideHk) onLogisticsworldwideHk();
        else if (this == Efex) onEfex();
        else if (this == Lotte) onLotte();
        else if (this == Lonestar) onLonestar();
        else if (this == Aprisaexpress) onAprisaexpress();
        else if (this == BelRs) onBelRs();
        else if (this == OsmWorldwide) onOsmWorldwide();
        else if (this == WestgateGl) onWestgateGl();
        else if (this == Fastrack) onFastrack();
        else if (this == DtdExpr) onDtdExpr();
        else if (this == Alfatrex) onAlfatrex();
        else if (this == Promeddelivery) onPromeddelivery();
        else if (this == ThabitLogistics) onThabitLogistics();
        else if (this == HctLogistics) onHctLogistics();
        else if (this == CarryFlap) onCarryFlap();
        else if (this == UsOldDominion) onUsOldDominion();
        else if (this == AnicamBox) onAnicamBox();
        else if (this == Wanbexpress) onWanbexpress();
        else if (this == AnPost) onAnPost();
        else if (this == DpdLocal) onDpdLocal();
        else if (this == Stallionexpress) onStallionexpress();
        else if (this == Raiderex) onRaiderex();
        else if (this == Shopfans) onShopfans();
        else if (this == KyungdongParcel) onKyungdongParcel();
        else if (this == ChampionLogistics) onChampionLogistics();
        else if (this == PickuppSgp) onPickuppSgp();
        else if (this == MorningExpress) onMorningExpress();
        else if (this == Nacex) onNacex();
        else if (this == ThenileWebhook) onThenileWebhook();
        else if (this == Holisol) onHolisol();
        else if (this == LbcexpressFtp) onLbcexpressFtp();
        else if (this == Kurasi) onKurasi();
        else if (this == UsfReddaway) onUsfReddaway();
        else if (this == Apg) onApg();
        else if (this == CnBoxc) onCnBoxc();
        else if (this == Ecoscooting) onEcoscooting();
        else if (this == Mainway) onMainway();
        else if (this == Paperfly) onPaperfly();
        else if (this == Houndexpress) onHoundexpress();
        else if (this == BoxBerry) onBoxBerry();
        else if (this == EpBox) onEpBox();
        else if (this == PlusLogUk) onPlusLogUk();
        else if (this == Fulfilla) onFulfilla();
        else if (this == Ase) onAse();
        else if (this == MailPlus) onMailPlus();
        else if (this == XpoLogistics) onXpoLogistics();
        else if (this == Wndirect) onWndirect();
        else if (this == CloudwishAsia) onCloudwishAsia();
        else if (this == Zeleris) onZeleris();
        else if (this == GioExpress) onGioExpress();
        else if (this == OcsWorldwide) onOcsWorldwide();
        else if (this == ArkLogistics) onArkLogistics();
        else if (this == Aquiline) onAquiline();
        else if (this == PilotFreight) onPilotFreight();
        else if (this == Qwintry) onQwintry();
        else if (this == DanskeFragt) onDanskeFragt();
        else if (this == Carriers) onCarriers();
        else if (this == AirCanadaGlobal) onAirCanadaGlobal();
        else if (this == PresidentTrans) onPresidentTrans();
        else if (this == Stepforwardfs) onStepforwardfs();
        else if (this == SkynetUk) onSkynetUk();
        else if (this == Pittohio) onPittohio();
        else if (this == CorreosExpress) onCorreosExpress();
        else if (this == RlUs) onRlUs();
        else if (this == Destiny) onDestiny();
        else if (this == UkYodel) onUkYodel();
        else if (this == CometTech) onCometTech();
        else if (this == DhlParcelRu) onDhlParcelRu();
        else if (this == TntRefr) onTntRefr();
        else if (this == ShreeAnjaniCourier) onShreeAnjaniCourier();
        else if (this == MikropakketBe) onMikropakketBe();
        else if (this == EtsExpress) onEtsExpress();
        else if (this == ColisPrive) onColisPrive();
        else if (this == CnYunda) onCnYunda();
        else if (this == AaaCooper) onAaaCooper();
        else if (this == RocketParcel) onRocketParcel();
        else if (this == _360Lion) on_360Lion();
        else if (this == Pandu) onPandu();
        else if (this == ProfessionalCouriers) onProfessionalCouriers();
        else if (this == Flytexpress) onFlytexpress();
        else if (this == LogisticsworldwideMy) onLogisticsworldwideMy();
        else if (this == CorreosDeEspana) onCorreosDeEspana();
        else if (this == Imx) onImx();
        else if (this == FourPxExpress) onFourPxExpress();
        else if (this == Xpressbees) onXpressbees();
        else if (this == PickuppVnm) onPickuppVnm();
        else if (this == StartrackExpress) onStartrackExpress();
        else if (this == FrColissimo) onFrColissimo();
        else if (this == NacexSpainReference) onNacexSpainReference();
        else if (this == DhlSupplyChainAu) onDhlSupplyChainAu();
        else if (this == Eshipping) onEshipping();
        else if (this == Shreetirupati) onShreetirupati();
        else if (this == HxExpress) onHxExpress();
        else if (this == Indopaket) onIndopaket();
        else if (this == Cn17Post) onCn17Post();
        else if (this == K1Express) onK1Express();
        else if (this == CjGls) onCjGls();
        else if (this == MysGdex) onMysGdex();
        else if (this == Nationex) onNationex();
        else if (this == Anjun) onAnjun();
        else if (this == Fargood) onFargood();
        else if (this == SmgExpress) onSmgExpress();
        else if (this == Rzyexpress) onRzyexpress();
        else if (this == Sefl) onSefl();
        else if (this == TntClickIt) onTntClickIt();
        else if (this == Hdb) onHdb();
        else if (this == Hipshipper) onHipshipper();
        else if (this == Rpxlogistics) onRpxlogistics();
        else if (this == Kuehne) onKuehne();
        else if (this == ItNexive) onItNexive();
        else if (this == Pts) onPts();
        else if (this == SwissPostFtp) onSwissPostFtp();
        else if (this == FastrkServ) onFastrkServ();
        else if (this == _472) on_472();
        else if (this == UsYrc) onUsYrc();
        else if (this == PostnlIntl3S) onPostnlIntl3S();
        else if (this == ElianPost) onElianPost();
        else if (this == Cubyn) onCubyn();
        else if (this == SauSaudiPost) onSauSaudiPost();
        else if (this == AbxexpressMy) onAbxexpressMy();
        else if (this == HuahanExpress) onHuahanExpress();
        else if (this == ZesExpress) onZesExpress();
        else if (this == ZeptoExpress) onZeptoExpress();
        else if (this == SkynetZa) onSkynetZa();
        else if (this == Zeek2Door) onZeek2Door();
        else if (this == Blinklastmile) onBlinklastmile();
        else if (this == PostaUkr) onPostaUkr();
        else if (this == Chrobinson) onChrobinson();
        else if (this == CnPost56) onCnPost56();
        else if (this == CourantPlus) onCourantPlus();
        else if (this == ScudexExpress) onScudexExpress();
        else if (this == Shipentegra) onShipentegra();
        else if (this == BTwoCEurope) onBTwoCEurope();
        else if (this == Cope) onCope();
        else if (this == IndGati) onIndGati();
        else if (this == CnWishpost) onCnWishpost();
        else if (this == NacexEs) onNacexEs();
        else if (this == TaqbinHk) onTaqbinHk();
        else if (this == Globaltranz) onGlobaltranz();
        else if (this == Hkd) onHkd();
        else if (this == Bjshomedelivery) onBjshomedelivery();
        else if (this == Omniva) onOmniva();
        else if (this == Sutton) onSutton();
        else if (this == PantherReference) onPantherReference();
        else if (this == Sfcservice) onSfcservice();
        else if (this == Ltl) onLtl();
        else if (this == Parknparcel) onParknparcel();
        else if (this == SpringGds) onSpringGds();
        else if (this == Ecexpress) onEcexpress();
        else if (this == InterparcelAu) onInterparcelAu();
        else if (this == Agility) onAgility();
        else if (this == XlExpress) onXlExpress();
        else if (this == Aderonline) onAderonline();
        else if (this == Directcouriers) onDirectcouriers();
        else if (this == Planzer) onPlanzer();
        else if (this == Sending) onSending();
        else if (this == NinjavanWb) onNinjavanWb();
        else if (this == NationwideMy) onNationwideMy();
        else if (this == Sendit) onSendit();
        else if (this == GbArrow) onGbArrow();
        else if (this == IndGojavas) onIndGojavas();
        else if (this == Kpost) onKpost();
        else if (this == DhlFreight) onDhlFreight();
        else if (this == Bluecare) onBluecare();
        else if (this == Jindouyun) onJindouyun();
        else if (this == Trackon) onTrackon();
        else if (this == GbTuffnells) onGbTuffnells();
        else if (this == Trumpcard) onTrumpcard();
        else if (this == Etotal) onEtotal();
        else if (this == SfplusWebhook) onSfplusWebhook();
        else if (this == Sekologistics) onSekologistics();
        else if (this == Hermes2MannHandling) onHermes2MannHandling();
        else if (this == DpdLocalRef) onDpdLocalRef();
        else if (this == Uds) onUds();
        else if (this == ZaSpecialisedFreight) onZaSpecialisedFreight();
        else if (this == ThaKerry) onThaKerry();
        else if (this == PrtIntSeur) onPrtIntSeur();
        else if (this == BraCorreios) onBraCorreios();
        else if (this == NzNzPost) onNzNzPost();
        else if (this == CnEquick) onCnEquick();
        else if (this == MysEms) onMysEms();
        else if (this == GbNorsk) onGbNorsk();
        else if (this == EspMrw) onEspMrw();
        else if (this == EspPacklink) onEspPacklink();
        else if (this == KangarooMy) onKangarooMy();
        else if (this == Rpx) onRpx();
        else if (this == XdpUkReference) onXdpUkReference();
        else if (this == NinjavanMy) onNinjavanMy();
        else if (this == Adicional) onAdicional();
        else if (this == Roadbull) onRoadbull();
        else if (this == Yakit) onYakit();
        else if (this == Mailamericas) onMailamericas();
        else if (this == Mikropakket) onMikropakket();
        else if (this == Dynalogic) onDynalogic();
        else if (this == DhlEs) onDhlEs();
        else if (this == DhlParcelNl) onDhlParcelNl();
        else if (this == DhlGlobalMailAsia) onDhlGlobalMailAsia();
        else if (this == DawnWing) onDawnWing();
        else if (this == GenikiGr) onGenikiGr();
        else if (this == HermesworldUk) onHermesworldUk();
        else if (this == Alphafast) onAlphafast();
        else if (this == Buylogic) onBuylogic();
        else if (this == Ekart) onEkart();
        else if (this == MexSenda) onMexSenda();
        else if (this == SfcLogistics) onSfcLogistics();
        else if (this == PostSerbia) onPostSerbia();
        else if (this == IndDelhivery) onIndDelhivery();
        else if (this == DeDpdDelistrack) onDeDpdDelistrack();
        else if (this == Rpd2Man) onRpd2Man();
        else if (this == CnSfExpress) onCnSfExpress();
        else if (this == Yanwen) onYanwen();
        else if (this == MysSkynet) onMysSkynet();
        else if (this == CorreosDeMexico) onCorreosDeMexico();
        else if (this == CblLogistica) onCblLogistica();
        else if (this == MexEstafeta) onMexEstafeta();
        else if (this == AuAustrianPost) onAuAustrianPost();
        else if (this == Rincos) onRincos();
        else if (this == NldDhl) onNldDhl();
        else if (this == RussianPost) onRussianPost();
        else if (this == CouriersPlease) onCouriersPlease();
        else if (this == PostnordLogistics) onPostnordLogistics();
        else if (this == Fedex) onFedex();
        else if (this == DpeExpress) onDpeExpress();
        else if (this == Dpd) onDpd();
        else if (this == Adsone) onAdsone();
        else if (this == IdnJne) onIdnJne();
        else if (this == Thecourierguy) onThecourierguy();
        else if (this == Cnexps) onCnexps();
        else if (this == PrtChronopost) onPrtChronopost();
        else if (this == LandmarkGlobal) onLandmarkGlobal();
        else if (this == ItDhlEcommerce) onItDhlEcommerce();
        else if (this == EspNacex) onEspNacex();
        else if (this == PrtCtt) onPrtCtt();
        else if (this == BeKiala) onBeKiala();
        else if (this == AsendiaUk) onAsendiaUk();
        else if (this == GlobalTnt) onGlobalTnt();
        else if (this == PosturIs) onPosturIs();
        else if (this == EparcelKr) onEparcelKr();
        else if (this == InpostPaczkomaty) onInpostPaczkomaty();
        else if (this == ItPosteItalia) onItPosteItalia();
        else if (this == BeBpost) onBeBpost();
        else if (this == PlPocztaPolska) onPlPocztaPolska();
        else if (this == MysMysPost) onMysMysPost();
        else if (this == SgSgPost) onSgSgPost();
        else if (this == ThaThailandPost) onThaThailandPost();
        else if (this == Lexship) onLexship();
        else if (this == FastwayNz) onFastwayNz();
        else if (this == DhlAu) onDhlAu();
        else if (this == Costmeticsnow) onCostmeticsnow();
        else if (this == Pflogistics) onPflogistics();
        else if (this == LoomisExpress) onLoomisExpress();
        else if (this == GlsItaly) onGlsItaly();
        else if (this == Line) onLine();
        else if (this == GelExpress) onGelExpress();
        else if (this == Huodull) onHuodull();
        else if (this == NinjavanSg) onNinjavanSg();
        else if (this == Janio) onJanio();
        else if (this == AoCourier) onAoCourier();
        else if (this == BrtItSenderRef) onBrtItSenderRef();
        else if (this == Sailpost) onSailpost();
        else if (this == Lalamove) onLalamove();
        else if (this == NewzealandCouriers) onNewzealandCouriers();
        else if (this == Etomars) onEtomars();
        else if (this == Virtransport) onVirtransport();
        else if (this == Wizmo) onWizmo();
        else if (this == Palletways) onPalletways();
        else if (this == IDika) onIDika();
        else if (this == CflLogistics) onCflLogistics();
        else if (this == Gemworldwide) onGemworldwide();
        else if (this == GlobalExpress) onGlobalExpress();
        else if (this == LogistyxTransgroup) onLogistyxTransgroup();
        else if (this == WestbankCourier) onWestbankCourier();
        else if (this == ArcoSpedizioni) onArcoSpedizioni();
        else if (this == YdhExpress) onYdhExpress();
        else if (this == Parcelinklogistics) onParcelinklogistics();
        else if (this == Cndexpress) onCndexpress();
        else if (this == NoxNightTimeExpress) onNoxNightTimeExpress();
        else if (this == Aeronet) onAeronet();
        else if (this == Ltianexp) onLtianexp();
        else if (this == Integra2Ftp) onIntegra2Ftp();
        else if (this == Parcelone) onParcelone();
        else if (this == NoxNachtexpress) onNoxNachtexpress();
        else if (this == CnChinaPostEms) onCnChinaPostEms();
        else if (this == Chukou1) onChukou1();
        else if (this == GlsSlov) onGlsSlov();
        else if (this == OrangeDs) onOrangeDs();
        else if (this == JoomLogis) onJoomLogis();
        else if (this == AusStartrack) onAusStartrack();
        else if (this == Dhl) onDhl();
        else if (this == GbApc) onGbApc();
        else if (this == Bondscouriers) onBondscouriers();
        else if (this == JpnJapanPost) onJpnJapanPost();
        else if (this == Usps) onUsps();
        else if (this == Winit) onWinit();
        else if (this == ArgOca) onArgOca();
        else if (this == TwTaiwanPost) onTwTaiwanPost();
        else if (this == DmmNetwork) onDmmNetwork();
        else if (this == Tnt) onTnt();
        else if (this == BhPosta) onBhPosta();
        else if (this == SwePostnord) onSwePostnord();
        else if (this == CaCanadaPost) onCaCanadaPost();
        else if (this == Wiseloads) onWiseloads();
        else if (this == AsendiaHk) onAsendiaHk();
        else if (this == NldGls) onNldGls();
        else if (this == MexRedpack) onMexRedpack();
        else if (this == JetShip) onJetShip();
        else if (this == DeDhlExpress) onDeDhlExpress();
        else if (this == NinjavanThai) onNinjavanThai();
        else if (this == RabenGroup) onRabenGroup();
        else if (this == EspAsm) onEspAsm();
        else if (this == HrvHrvatska) onHrvHrvatska();
        else if (this == GlobalEstes) onGlobalEstes();
        else if (this == LtuLietuvos) onLtuLietuvos();
        else if (this == BelDhl) onBelDhl();
        else if (this == AuAuPost) onAuAuPost();
        else if (this == Speedexcourier) onSpeedexcourier();
        else if (this == FrColis) onFrColis();
        else if (this == Aramex) onAramex();
        else if (this == Dpex) onDpex();
        else if (this == MysAirpak) onMysAirpak();
        else if (this == Cuckooexpress) onCuckooexpress();
        else if (this == DpdPoland) onDpdPoland();
        else if (this == NldPostnl) onNldPostnl();
        else if (this == NimExpress) onNimExpress();
        else if (this == Quantium) onQuantium();
        else if (this == Sendle) onSendle();
        else if (this == EspRedur) onEspRedur();
        else if (this == Matkahuolto) onMatkahuolto();
        else if (this == Cpacket) onCpacket();
        else if (this == Posti) onPosti();
        else if (this == HunterExpress) onHunterExpress();
        else if (this == ChoirExp) onChoirExp();
        else if (this == LegionExpress) onLegionExpress();
        else if (this == AustrianPostExpress) onAustrianPostExpress();
        else if (this == Grupo) onGrupo();
        else if (this == PostaRo) onPostaRo();
        else if (this == InterparcelUk) onInterparcelUk();
        else if (this == GlobalAbf) onGlobalAbf();
        else if (this == PostenNorge) onPostenNorge();
        else if (this == XpertDelivery) onXpertDelivery();
        else if (this == DhlRefr) onDhlRefr();
        else if (this == DhlHk) onDhlHk();
        else if (this == SkynetUae) onSkynetUae();
        else if (this == Gojek) onGojek();
        else if (this == YodelIntnl) onYodelIntnl();
        else if (this == Janco) onJanco();
        else if (this == Yto) onYto();
        else if (this == WiseExpress) onWiseExpress();
        else if (this == JtexpressVn) onJtexpressVn();
        else if (this == FedexIntlMlserv) onFedexIntlMlserv();
        else if (this == Vamox) onVamox();
        else if (this == AmsGrp) onAmsGrp();
        else if (this == DhlJp) onDhlJp();
        else if (this == Hrparcel) onHrparcel();
        else if (this == Geswl) onGeswl();
        else if (this == Bluestar) onBluestar();
        else if (this == CdekTr) onCdekTr();
        else if (this == Descartes) onDescartes();
        else if (this == DeltecUk) onDeltecUk();
        else if (this == DtdcExpress) onDtdcExpress();
        else if (this == Tourline) onTourline();
        else if (this == BhWorldwide) onBhWorldwide();
        else if (this == Ocs) onOcs();
        else if (this == YingnuoLogistics) onYingnuoLogistics();
        else if (this == Ups) onUps();
        else if (this == Toll) onToll();
        else if (this == PrtSeur) onPrtSeur();
        else if (this == DtdcAu) onDtdcAu();
        else if (this == ThaDynamicLogistics) onThaDynamicLogistics();
        else if (this == UbiLogistics) onUbiLogistics();
        else if (this == FedexCrossborder) onFedexCrossborder();
        else if (this == A1Post) onA1Post();
        else if (this == TazmanianFreight) onTazmanianFreight();
        else if (this == CjIntMy) onCjIntMy();
        else if (this == SaiaFreight) onSaiaFreight();
        else if (this == SgQxpress) onSgQxpress();
        else if (this == NhansSolutions) onNhansSolutions();
        else if (this == DpdFr) onDpdFr();
        else if (this == Coordinadora) onCoordinadora();
        else if (this == Andreani) onAndreani();
        else if (this == Doora) onDoora();
        else if (this == InterparcelNz) onInterparcelNz();
        else if (this == PhlJamexpress) onPhlJamexpress();
        else if (this == BelBelgiumPost) onBelBelgiumPost();
        else if (this == UsApc) onUsApc();
        else if (this == IdnPos) onIdnPos();
        else if (this == FrMondial) onFrMondial();
        else if (this == DeDhl) onDeDhl();
        else if (this == HkRpx) onHkRpx();
        else if (this == DhlPieceid) onDhlPieceid();
        else if (this == VnpostEms) onVnpostEms();
        else if (this == Rrdonnelley) onRrdonnelley();
        else if (this == DpdDe) onDpdDe();
        else if (this == DelcartIn) onDelcartIn();
        else if (this == Imexglobalsolutions) onImexglobalsolutions();
        else if (this == Acommerce) onAcommerce();
        else if (this == Eurodis) onEurodis();
        else if (this == Canpar) onCanpar();
        else if (this == Gls) onGls();
        else if (this == IndEcom) onIndEcom();
        else if (this == EspEnvialia) onEspEnvialia();
        else if (this == DhlUk) onDhlUk();
        else if (this == SmsaExpress) onSmsaExpress();
        else if (this == TntFr) onTntFr();
        else if (this == DexI) onDexI();
        else if (this == BudbeeWebhook) onBudbeeWebhook();
        else if (this == CopaCourier) onCopaCourier();
        else if (this == VnmVietnamPost) onVnmVietnamPost();
        else if (this == DpdHk) onDpdHk();
        else if (this == TollNz) onTollNz();
        else if (this == Echo) onEcho();
        else if (this == FedexFr) onFedexFr();
        else if (this == Borderexpress) onBorderexpress();
        else if (this == MailplusJpn) onMailplusJpn();
        else if (this == TntUkRefr) onTntUkRefr();
        else if (this == Kec) onKec();
        else if (this == DpdRo) onDpdRo();
        else if (this == TntJp) onTntJp();
        else if (this == ThCj) onThCj();
        else if (this == EcCn) onEcCn();
        else if (this == FastwayUk) onFastwayUk();
        else if (this == FastwayUs) onFastwayUs();
        else if (this == GlsDe) onGlsDe();
        else if (this == GlsEs) onGlsEs();
        else if (this == GlsFr) onGlsFr();
        else if (this == MondialBe) onMondialBe();
        else if (this == SgtIt) onSgtIt();
        else if (this == TntCn) onTntCn();
        else if (this == TntDe) onTntDe();
        else if (this == TntEs) onTntEs();
        else if (this == TntPl) onTntPl();
        else if (this == Parcelforce) onParcelforce();
        else if (this == SwissPost) onSwissPost();
        else if (this == TollIpec) onTollIpec();
        else if (this == Air21) onAir21();
        else if (this == Airspeed) onAirspeed();
        else if (this == Bert) onBert();
        else if (this == Bluedart) onBluedart();
        else if (this == Collectplus) onCollectplus();
        else if (this == Courierplus) onCourierplus();
        else if (this == CourierPost) onCourierPost();
        else if (this == DhlGlobalMail) onDhlGlobalMail();
        else if (this == DpdUk) onDpdUk();
        else if (this == DeltecDe) onDeltecDe();
        else if (this == DeutscheDe) onDeutscheDe();
        else if (this == Dotzot) onDotzot();
        else if (this == EltaGr) onEltaGr();
        else if (this == EmsCn) onEmsCn();
        else if (this == Ecargo) onEcargo();
        else if (this == Ensenda) onEnsenda();
        else if (this == FercamIt) onFercamIt();
        else if (this == FastwayZa) onFastwayZa();
        else if (this == FastwayAu) onFastwayAu();
        else if (this == FirstLogisitcs) onFirstLogisitcs();
        else if (this == Geodis) onGeodis();
        else if (this == Globegistics) onGlobegistics();
        else if (this == Greyhound) onGreyhound();
        else if (this == JetshipMy) onJetshipMy();
        else if (this == LionParcel) onLionParcel();
        else if (this == Aeroflash) onAeroflash();
        else if (this == Ontrac) onOntrac();
        else if (this == Sagawa) onSagawa();
        else if (this == Siodemka) onSiodemka();
        else if (this == Startrack) onStartrack();
        else if (this == TntAu) onTntAu();
        else if (this == TntIt) onTntIt();
        else if (this == Transmission) onTransmission();
        else if (this == Yamato) onYamato();
        else if (this == DhlIt) onDhlIt();
        else if (this == DhlAt) onDhlAt();
        else if (this == LogisticsworldwideKr) onLogisticsworldwideKr();
        else if (this == GlsSpain) onGlsSpain();
        else if (this == AmazonUkApi) onAmazonUkApi();
        else if (this == DpdFrReference) onDpdFrReference();
        else if (this == DhlparcelUk) onDhlparcelUk();
        else if (this == Megasave) onMegasave();
        else if (this == Qualitypost) onQualitypost();
        else if (this == IdsLogistics) onIdsLogistics();
        else if (this == Joyingbox) onJoyingbox();
        else if (this == PantherOrderNumber) onPantherOrderNumber();
        else if (this == WatkinsShepard) onWatkinsShepard();
        else if (this == Fasttrack) onFasttrack();
        else if (this == UpExpress) onUpExpress();
        else if (this == Elogistica) onElogistica();
        else if (this == Ecourier) onEcourier();
        else if (this == CjPhilippines) onCjPhilippines();
        else if (this == Speedex) onSpeedex();
        else if (this == Orangeconnex) onOrangeconnex();
        else if (this == Tecor) onTecor();
        else if (this == Saee) onSaee();
        else if (this == GlsItalyFtp) onGlsItalyFtp();
        else if (this == Delivere) onDelivere();
        else if (this == Yycom) onYycom();
        else if (this == AdicionalPt) onAdicionalPt();
        else if (this == Dksh) onDksh();
        else if (this == NipponExpressFtp) onNipponExpressFtp();
        else if (this == Gols) onGols();
        else if (this == Fujexp) onFujexp();
        else if (this == Qtrack) onQtrack();
        else if (this == OmlogisticsApi) onOmlogisticsApi();
        else if (this == Gdpharm) onGdpharm();
        else if (this == MisumiCn) onMisumiCn();
        else if (this == AirCanada) onAirCanada();
        else if (this == City56Webhook) onCity56Webhook();
        else if (this == SagawaApi) onSagawaApi();
        else if (this == Kedaex) onKedaex();
        else if (this == PgeonApi) onPgeonApi();
        else if (this == Weworldexpress) onWeworldexpress();
        else if (this == JtLogistics) onJtLogistics();
        else if (this == Trusk) onTrusk();
        else if (this == Viaxpress) onViaxpress();
        else if (this == DhlSupplychainId) onDhlSupplychainId();
        else if (this == ZuelligpharmaSftp) onZuelligpharmaSftp();
        else if (this == Meest) onMeest();
        else if (this == TollPriority) onTollPriority();
        else if (this == MothershipApi) onMothershipApi();
        else if (this == Capital) onCapital();
        else if (this == EuropaketApi) onEuropaketApi();
        else if (this == Hfd) onHfd();
        else if (this == TourlineReference) onTourlineReference();
        else if (this == GioEcourier) onGioEcourier();
        else if (this == CnLogistics) onCnLogistics();
        else if (this == Pandion) onPandion();
        else if (this == BpostApi) onBpostApi();
        else if (this == Passportshipping) onPassportshipping();
        else if (this == Pakajo) onPakajo();
        else if (this == Dachser) onDachser();
        else if (this == YusenSftp) onYusenSftp();
        else if (this == Shyplite) onShyplite();
        else if (this == Xyy) onXyy();
        else if (this == Mwd) onMwd();
        else if (this == Faxecargo) onFaxecargo();
        else if (this == Mazet) onMazet();
        else if (this == FirstLogisticsApi) onFirstLogisticsApi();
        else if (this == SprintPack) onSprintPack();
        else if (this == HermesDeFtp) onHermesDeFtp();
        else if (this == Concise) onConcise();
        else if (this == KerryExpressTwApi) onKerryExpressTwApi();
        else if (this == Ewe) onEwe();
        else if (this == Fastdespatch) onFastdespatch();
        else if (this == AbcustomSftp) onAbcustomSftp();
        else if (this == Chazki) onChazki();
        else if (this == Shippie) onShippie();
        else if (this == GeodisApi) onGeodisApi();
        else if (this == NaqelExpress) onNaqelExpress();
        else if (this == PapaWebhook) onPapaWebhook();
        else if (this == Forwardair) onForwardair();
        else if (this == DialogoLogisticaApi) onDialogoLogisticaApi();
        else if (this == LalamoveApi) onLalamoveApi();
        else if (this == Tomydoor) onTomydoor();
        else if (this == KronosWebhook) onKronosWebhook();
        else if (this == Jtcargo) onJtcargo();
        else if (this == TCat) onTCat();
        else if (this == ConciseWebhook) onConciseWebhook();
        else if (this == TeleportWebhook) onTeleportWebhook();
        else if (this == CustomcoApi) onCustomcoApi();
        else if (this == SpxTh) onSpxTh();
        else if (this == BolloreLogistics) onBolloreLogistics();
        else if (this == ClicklinkSftp) onClicklinkSftp();
        else if (this == M3Logistics) onM3Logistics();
        else if (this == VnpostApi) onVnpostApi();
        else if (this == AxlehireFtp) onAxlehireFtp();
        else if (this == Shadowfax) onShadowfax();
        else if (this == MyhermesUkApi) onMyhermesUkApi();
        else if (this == Daiichi) onDaiichi();
        else if (this == MensajerosurbanosApi) onMensajerosurbanosApi();
        else if (this == Polarspeed) onPolarspeed();
        else if (this == IdexpressId) onIdexpressId();
        else if (this == Payo) onPayo();
        else if (this == WhistlSftp) onWhistlSftp();
        else if (this == IntexDe) onIntexDe();
        else if (this == Trans2U) onTrans2U();
        else if (this == ProductcaregroupSftp) onProductcaregroupSftp();
        else if (this == Bigsmart) onBigsmart();
        else if (this == ExpeditorsApiRef) onExpeditorsApiRef();
        else if (this == AitworldwideApi) onAitworldwideApi();
        else if (this == Worldcourier) onWorldcourier();
        else if (this == Quiqup) onQuiqup();
        else if (this == AgedissSftp) onAgedissSftp();
        else if (this == AndreaniApi) onAndreaniApi();
        else if (this == Crlexpress) onCrlexpress();
        else if (this == Smartcat) onSmartcat();
        else if (this == Crossflight) onCrossflight();
        else if (this == Procarrier) onProcarrier();
        else if (this == DhlReferenceApi) onDhlReferenceApi();
        else if (this == SeinoApi) onSeinoApi();
        else if (this == Wspexpress) onWspexpress();
        else if (this == Kronos) onKronos();
        else if (this == TotalExpressApi) onTotalExpressApi();
        else if (this == Parcll) onParcll();
        else if (this == Xpedigo) onXpedigo();
        else if (this == StarTrackWebhook) onStarTrackWebhook();
        else if (this == Gpost) onGpost();
        else if (this == Ucs) onUcs();
        else if (this == Dmfgroup) onDmfgroup();
        else if (this == CoordinadoraApi) onCoordinadoraApi();
        else if (this == Marken) onMarken();
        else if (this == Ntl) onNtl();
        else if (this == Redjepakketje) onRedjepakketje();
        else if (this == AlliedExpressFtp) onAlliedExpressFtp();
        else if (this == MondialrelayEs) onMondialrelayEs();
        else if (this == NaekoFtp) onNaekoFtp();
        else if (this == Mhi) onMhi();
        else if (this == Shippify) onShippify();
        else if (this == MalcaAmitApi) onMalcaAmitApi();
        else if (this == JtexpressSgApi) onJtexpressSgApi();
        else if (this == DachserWeb) onDachserWeb();
        else if (this == Flightlg) onFlightlg();
        else if (this == Cago) onCago();
        else if (this == Com1Express) onCom1Express();
        else if (this == TonamiFtp) onTonamiFtp();
        else if (this == Packfleet) onPackfleet();
        else if (this == PurolatorInternational) onPurolatorInternational();
        else if (this == WineshippingWebhook) onWineshippingWebhook();
        else if (this == DhlEsSftp) onDhlEsSftp();
        else if (this == PchomeApi) onPchomeApi();
        else if (this == CeskapostaApi) onCeskapostaApi();
        else if (this == Gorush) onGorush();
        else if (this == Homerunner) onHomerunner();
        else if (this == AmazonOrder) onAmazonOrder();
        else if (this == EfwnowApi) onEfwnowApi();
        else if (this == CblLogisticaApi) onCblLogisticaApi();
        else if (this == Nimbuspost) onNimbuspost();
        else if (this == LogwinLogistics) onLogwinLogistics();
        else if (this == NowlogApi) onNowlogApi();
        else if (this == DpdNl) onDpdNl();
        else if (this == Godependable) onGodependable();
        else if (this == Esdex) onEsdex();
        else if (this == LogisystemsSftp) onLogisystemsSftp();
        else if (this == Expeditors) onExpeditors();
        else if (this == SntglobalApi) onSntglobalApi();
        else if (this == Shipx) onShipx();
        else if (this == QintlApi) onQintlApi();
        else if (this == Packs) onPacks();
        else if (this == PostnlInternational) onPostnlInternational();
        else if (this == AmazonEmailPush) onAmazonEmailPush();
        else if (this == DhlApi) onDhlApi();
        else if (this == Spx) onSpx();
        else if (this == Axlehire) onAxlehire();
        else if (this == Icscourier) onIcscourier();
        else if (this == DialogoLogistica) onDialogoLogistica();
        else if (this == ShunbangExpress) onShunbangExpress();
        else if (this == TcsApi) onTcsApi();
        else if (this == SfExpressCn) onSfExpressCn();
        else if (this == Packeta) onPacketa();
        else if (this == SicTeliway) onSicTeliway();
        else if (this == MondialrelayFr) onMondialrelayFr();
        else if (this == IntimeFtp) onIntimeFtp();
        else if (this == JdExpress) onJdExpress();
        else if (this == Fastbox) onFastbox();
        else if (this == Patheon) onPatheon();
        else if (this == IndiaPost) onIndiaPost();
        else if (this == TipsaRef) onTipsaRef();
        else if (this == Ecofreight) onEcofreight();
        else if (this == Vox) onVox();
        else if (this == DirectfreightAuRef) onDirectfreightAuRef();
        else if (this == BesttransportSftp) onBesttransportSftp();
        else if (this == AustraliaPostApi) onAustraliaPostApi();
        else if (this == FragilepakSftp) onFragilepakSftp();
        else if (this == Flipxp) onFlipxp();
        else if (this == ValueWebhook) onValueWebhook();
        else if (this == Daeshin) onDaeshin();
        else if (this == Sherpa) onSherpa();
        else if (this == MwdApi) onMwdApi();
        else if (this == Smartkargo) onSmartkargo();
        else if (this == DnjExpress) onDnjExpress();
        else if (this == Gopeople) onGopeople();
        else if (this == MysendleApi) onMysendleApi();
        else if (this == AramexApi) onAramexApi();
        else if (this == Pidge) onPidge();
        else if (this == Thaiparcels) onThaiparcels();
        else if (this == PantherReferenceApi) onPantherReferenceApi();
        else if (this == Postaplus) onPostaplus();
        else if (this == Buffalo) onBuffalo();
        else if (this == UEnvios) onUEnvios();
        else if (this == EliteCo) onEliteCo();
        else if (this == RocheInternalSftp) onRocheInternalSftp();
        else if (this == DbschenkerIceland) onDbschenkerIceland();
        else if (this == TntFrReference) onTntFrReference();
        else if (this == Newgisticsapi) onNewgisticsapi();
        else if (this == Glovo) onGlovo();
        else if (this == GwlogisApi) onGwlogisApi();
        else if (this == SpreetailApi) onSpreetailApi();
        else if (this == Moova) onMoova();
        else if (this == Plycongroup) onPlycongroup();
        else if (this == UspsWebhook) onUspsWebhook();
        else if (this == Reimaginedelivery) onReimaginedelivery();
        else if (this == EdfFtp) onEdfFtp();
        else if (this == Dao365) onDao365();
        else if (this == BiocairFtp) onBiocairFtp();
        else if (this == RansaWebhook) onRansaWebhook();
        else if (this == Shipxpres) onShipxpres();
        else if (this == CourantPlusApi) onCourantPlusApi();
        else if (this == Shipa) onShipa();
        else if (this == Homelogistics) onHomelogistics();
        else if (this == Dx) onDx();
        else if (this == PosteItalianePaccocelere) onPosteItalianePaccocelere();
        else if (this == TollWebhook) onTollWebhook();
        else if (this == LctbrApi) onLctbrApi();
        else if (this == DxFreight) onDxFreight();
        else if (this == DhlSftp) onDhlSftp();
        else if (this == Shiprocket) onShiprocket();
        else if (this == UberWebhook) onUberWebhook();
        else if (this == Statovernight) onStatovernight();
        else if (this == Burd) onBurd();
        else if (this == Fastship) onFastship();
        else if (this == IbventureWebhook) onIbventureWebhook();
        else if (this == GatiKweApi) onGatiKweApi();
        else if (this == CryopdpFtp) onCryopdpFtp();
        else if (this == Hubbed) onHubbed();
        else if (this == TipsaApi) onTipsaApi();
        else if (this == Araskargo) onAraskargo();
        else if (this == ThijsNl) onThijsNl();
        else if (this == AtshealthcareReference) onAtshealthcareReference();
        else if (this == _99Minutos) on_99Minutos();
        else if (this == HellenicPost) onHellenicPost();
        else if (this == HsmGlobal) onHsmGlobal();
        else if (this == Mnx) onMnx();
        else if (this == Nmtransfer) onNmtransfer();
        else if (this == Logysto) onLogysto();
        else if (this == IndiaPostInt) onIndiaPostInt();
        else if (this == AmazonFbaSwishipIn) onAmazonFbaSwishipIn();
        else if (this == SrtTransport) onSrtTransport();
        else if (this == Bomi) onBomi();
        else if (this == DeliverrSftp) onDeliverrSftp();
        else if (this == Hsdexpress) onHsdexpress();
        else if (this == SimpletireWebhook) onSimpletireWebhook();
        else if (this == HunterExpressSftp) onHunterExpressSftp();
        else if (this == UpsApi) onUpsApi();
        else if (this == WooyoungLogisticsSftp) onWooyoungLogisticsSftp();
        else if (this == PhseApi) onPhseApi();
        else if (this == WishEmailPush) onWishEmailPush();
        else if (this == Northline) onNorthline();
        else if (this == Medafrica) onMedafrica();
        else if (this == DpdAtSftp) onDpdAtSftp();
        else if (this == Anteraja) onAnteraja();
        else if (this == DhlGlobalForwardingApi) onDhlGlobalForwardingApi();
        else if (this == LbcexpressApi) onLbcexpressApi();
        else if (this == Simsglobal) onSimsglobal();
        else if (this == Cdldelivers) onCdldelivers();
        else if (this == Typ) onTyp();
        else if (this == TestingCourierWebhook) onTestingCourierWebhook();
        else if (this == PandagoApi) onPandagoApi();
        else if (this == RoyalMailFtp) onRoyalMailFtp();
        else if (this == Thunderexpress) onThunderexpress();
        else if (this == SecretlabWebhook) onSecretlabWebhook();
        else if (this == Setel) onSetel();
        else if (this == JdWorldwide) onJdWorldwide();
        else if (this == DpdRuApi) onDpdRuApi();
        else if (this == ArgentsWebhook) onArgentsWebhook();
        else if (this == Postone) onPostone();
        else if (this == Tusklogistics) onTusklogistics();
        else if (this == RhenusUkApi) onRhenusUkApi();
        else if (this == TaqbinSgApi) onTaqbinSgApi();
        else if (this == InntralogSftp) onInntralogSftp();
        else if (this == Dayross) onDayross();
        else if (this == CorreosexpressApi) onCorreosexpressApi();
        else if (this == InternationalSeurApi) onInternationalSeurApi();
        else if (this == YodelApi) onYodelApi();
        else if (this == Heroexpress) onHeroexpress();
        else if (this == DhlSupplychainIn) onDhlSupplychainIn();
        else if (this == UrgentCargus) onUrgentCargus();
        else if (this == Frontdoorcorp) onFrontdoorcorp();
        else if (this == JtexpressPh) onJtexpressPh();
        else if (this == ParcelstarsWebhook) onParcelstarsWebhook();
        else if (this == DpdSkSftp) onDpdSkSftp();
        else if (this == Movianto) onMovianto();
        else if (this == OzepartsShipping) onOzepartsShipping();
        else if (this == Kargomkolay) onKargomkolay();
        else if (this == Trunkrs) onTrunkrs();
        else if (this == OmnirpsWebhook) onOmnirpsWebhook();
        else if (this == Chilexpress) onChilexpress();
        else if (this == TestingCourier) onTestingCourier();
        else if (this == JneApi) onJneApi();
        else if (this == BjshomedeliveryFtp) onBjshomedeliveryFtp();
        else if (this == DexpressWebhook) onDexpressWebhook();
        else if (this == UspsApi) onUspsApi();
        else if (this == Transvirtual) onTransvirtual();
        else if (this == SolisticaApi) onSolisticaApi();
        else if (this == ChienventureWebhook) onChienventureWebhook();
        else if (this == DpdUkSftp) onDpdUkSftp();
        else if (this == InpostUk) onInpostUk();
        else if (this == Javit) onJavit();
        else if (this == ZtoDomestic) onZtoDomestic();
        else if (this == DhlGtApi) onDhlGtApi();
        else if (this == CevaTracking) onCevaTracking();
        else if (this == KomonExpress) onKomonExpress();
        else if (this == EastwestcourierFtp) onEastwestcourierFtp();
        else if (this == Danniao) onDanniao();
        else if (this == Spectran) onSpectran();
        else if (this == DeliverIt) onDeliverIt();
        else if (this == Relaiscolis) onRelaiscolis();
        else if (this == GlsSpainApi) onGlsSpainApi();
        else if (this == Postplus) onPostplus();
        else if (this == Airterra) onAirterra();
        else if (this == GioEcourierApi) onGioEcourierApi();
        else if (this == DpdChSftp) onDpdChSftp();
        else if (this == FedexApi) onFedexApi();
        else if (this == Intersmarttrans) onIntersmarttrans();
        else if (this == HermesUkSftp) onHermesUkSftp();
        else if (this == ExelotFtp) onExelotFtp();
        else if (this == DhlPaApi) onDhlPaApi();
        else if (this == VirtransportSftp) onVirtransportSftp();
        else if (this == Worldnet) onWorldnet();
        else if (this == InstaboxWebhook) onInstaboxWebhook();
        else if (this == Kng) onKng();
        else if (this == FlashexpressWebhook) onFlashexpressWebhook();
        else if (this == MagyarPostaApi) onMagyarPostaApi();
        else if (this == WeshipApi) onWeshipApi();
        else if (this == OhiWebhook) onOhiWebhook();
        else if (this == Mudita) onMudita();
        else if (this == BluedartApi) onBluedartApi();
        else if (this == TCatApi) onTCatApi();
        else if (this == Ads) onAds();
        else if (this == HermesIt) onHermesIt();
        else if (this == FitzmarkApi) onFitzmarkApi();
        else if (this == PostiApi) onPostiApi();
        else if (this == SmsaExpressWebhook) onSmsaExpressWebhook();
        else if (this == TamergroupWebhook) onTamergroupWebhook();
        else if (this == Livrapide) onLivrapide();
        else if (this == NipponExpress) onNipponExpress();
        else if (this == Bettertrucks) onBettertrucks();
        else if (this == Fan) onFan();
        else if (this == PbUspsflatsFtp) onPbUspsflatsFtp();
        else if (this == Parcelright) onParcelright();
        else if (this == Ithinklogistics) onIthinklogistics();
        else if (this == KerryExpressThWebhook) onKerryExpressThWebhook();
        else if (this == Ecoutier) onEcoutier();
        else if (this == Showl) onShowl();
        else if (this == BrtItApi) onBrtItApi();
        else if (this == RixonhkApi) onRixonhkApi();
        else if (this == DbschenkerApi) onDbschenkerApi();
        else if (this == Ilyanglogis) onIlyanglogis();
        else if (this == MailBoxEtc) onMailBoxEtc();
        else if (this == Weship) onWeship();
        else if (this == DhlGlobalMailApi) onDhlGlobalMailApi();
        else if (this == Activos24Api) onActivos24Api();
        else if (this == Atshealthcare) onAtshealthcare();
        else if (this == Luwjistik) onLuwjistik();
        else if (this == GwWorld) onGwWorld();
        else if (this == FairsendenApi) onFairsendenApi();
        else if (this == ServipWebhook) onServipWebhook();
        else if (this == Swiship) onSwiship();
        else if (this == Tanet) onTanet();
        else if (this == HotsinCargo) onHotsinCargo();
        else if (this == Direx) onDirex();
        else if (this == Huantong) onHuantong();
        else if (this == ImileApi) onImileApi();
        else if (this == Auexpress) onAuexpress();
        else if (this == Nytlogistics) onNytlogistics();
        else if (this == DsvReference) onDsvReference();
        else if (this == NovofarmaWebhook) onNovofarmaWebhook();
        else if (this == AitworldwideSftp) onAitworldwideSftp();
        else if (this == Shopolive) onShopolive();
        else if (this == FnfZa) onFnfZa();
        else if (this == DhlEcommerceGc) onDhlEcommerceGc();
        else if (this == Fetchr) onFetchr();
        else if (this == StarlinksApi) onStarlinksApi();
        else if (this == Yyexpress) onYyexpress();
        else if (this == Servientrega) onServientrega();
        else if (this == Hanjin) onHanjin();
        else if (this == SpanishSeurFtp) onSpanishSeurFtp();
        else if (this == DxB2BConnum) onDxB2BConnum();
        else if (this == HelthjemApi) onHelthjemApi();
        else if (this == Inexpost) onInexpost();
        else if (this == A2BBa) onA2BBa();
        else if (this == RhenusGroup) onRhenusGroup();
        else if (this == SberlogisticsRu) onSberlogisticsRu();
        else if (this == MalcaAmit) onMalcaAmit();
        else if (this == Ppl) onPpl();
        else if (this == OsmWorldwideSftp) onOsmWorldwideSftp();
        else if (this == Acilogistix) onAcilogistix();
        else if (this == Optimacourier) onOptimacourier();
        else if (this == NovaPoshtaApi) onNovaPoshtaApi();
        else if (this == Loggi) onLoggi();
        else if (this == Yifan) onYifan();
        else if (this == Mydynalogic) onMydynalogic();
        else if (this == Morninglobal) onMorninglobal();
        else if (this == ConciseApi) onConciseApi();
        else if (this == Fxtran) onFxtran();
        else if (this == DeliveryourparcelZa) onDeliveryourparcelZa();
        else if (this == Uparcel) onUparcel();
        else if (this == MobiBr) onMobiBr();
        else if (this == LoginextWebhook) onLoginextWebhook();
        else if (this == Ems) onEms();
        else if (this == Speedy) onSpeedy();
        else if (this == ZoomRed) onZoomRed();
        else if (this == Navlungo) onNavlungo();
        else if (this == Castleparcels) onCastleparcels();
        else if (this == Weee) onWeee();
        else if (this == Packaly) onPackaly();
        else if (this == Yunhuipost) onYunhuipost();
        else if (this == Youparcel) onYouparcel();
        else if (this == Leman) onLeman();
        else if (this == Moovin) onMoovin();
        else if (this == UrbIt) onUrbIt();
        else if (this == Multientregapanama) onMultientregapanama();
        else if (this == Jusdasr) onJusdasr();
        else if (this == Discountpost) onDiscountpost();
        else if (this == RhenusUk) onRhenusUk();
        else if (this == SwishipJp) onSwishipJp();
        else if (this == GlsUs) onGlsUs();
        else if (this == Smtl) onSmtl();
        else if (this == Emega) onEmega();
        else if (this == ExpressoneSv) onExpressoneSv();
        else if (this == Hepsijet) onHepsijet();
        else if (this == Welivery) onWelivery();
        else if (this == Bringer) onBringer();
        else if (this == Easyroutes) onEasyroutes();
        else if (this == Mrw) onMrw();
        else if (this == Rpm) onRpm();
        else if (this == DpdPrt) onDpdPrt();
        else if (this == GlsRomania) onGlsRomania();
        else if (this == Lmparcel) onLmparcel();
        else if (this == Gtagsm) onGtagsm();
        else if (this == Domino) onDomino();
        else if (this == Eshipper) onEshipper();
        else if (this == Transpak) onTranspak();
        else if (this == Xindus) onXindus();
        else if (this == Aoyue) onAoyue();
        else if (this == Easyparcel) onEasyparcel();
        else if (this == Expressone) onExpressone();
        else if (this == SendeoKargo) onSendeoKargo();
        else if (this == Speedaf) onSpeedaf();
        else if (this == Etower) onEtower();
        else if (this == Gcx) onGcx();
        else if (this == NinjavanVn) onNinjavanVn();
        else if (this == Allegro) onAllegro();
        else if (this == Jumppoint) onJumppoint();
        else if (this == ShipglobalUs) onShipglobalUs();
        else if (this == Kinisi) onKinisi();
        else if (this == Oakh) onOakh();
        else if (this == Awest) onAwest();
        else if (this == Barsan) onBarsan();
        else if (this == Energologistic) onEnergologistic();
        else if (this == Madrooex) onMadrooex();
        else if (this == Gobolt) onGobolt();
        else if (this == SwissUniversalExpress) onSwissUniversalExpress();
        else if (this == Iordirect) onIordirect();
        else if (this == Xmszm) onXmszm();
        else if (this == GlsHun) onGlsHun();
        else if (this == Sendy) onSendy();
        else if (this == Braunsexpress) onBraunsexpress();
        else if (this == Grandslamexpress) onGrandslamexpress();
        else if (this == Xgs) onXgs();
        else if (this == Otschile) onOtschile();
        else if (this == PackUp) onPackUp();
        else if (this == Parcelstars) onParcelstars();
        else if (this == Teamexpressllc) onTeamexpressllc();
        else if (this == Asyadexpress) onAsyadexpress();
        else if (this == Tdn) onTdn();
        else if (this == Earlybird) onEarlybird();
        else if (this == Cacesa) onCacesa();
        else if (this == Parceljet) onParceljet();
        else if (this == MngKargo) onMngKargo();
        else if (this == Superpackline) onSuperpackline();
        else if (this == Speedx) onSpeedx();
        else if (this == Vesyl) onVesyl();
        else if (this == Skyking) onSkyking();
        else if (this == Dirmensajeria) onDirmensajeria();
        else if (this == Netlogixgroup) onNetlogixgroup();
        else if (this == Zyou) onZyou();
        else if (this == Jawar) onJawar();
        else if (this == Agsystems) onAgsystems();
        else if (this == Gps) onGps();
        else if (this == PttKargo) onPttKargo();
        else if (this == Maergo) onMaergo();
        else if (this == Arihantcourier) onArihantcourier();
        else if (this == Vtfe) onVtfe();
        else if (this == Yunant) onYunant();
        else if (this == Urbify) onUrbify();
        else if (this == PackMan) onPackMan();
        else if (this == Liefergrun) onLiefergrun();
        else if (this == Obibox) onObibox();
        else if (this == Paikeda) onPaikeda();
        else if (this == Scotty) onScotty();
        else if (this == IntelcomCa) onIntelcomCa();
        else if (this == Swe) onSwe();
        else if (this == Asendia) onAsendia();
        else if (this == DpdAt) onDpdAt();
        else if (this == Relay) onRelay();
        else if (this == Ata) onAta();
        else if (this == SkyexpressInternational) onSkyexpressInternational();
        else if (this == SuratKargo) onSuratKargo();
        else if (this == Sglink) onSglink();
        else if (this == Fleetopticsinc) onFleetopticsinc();
        else if (this == Shopline) onShopline();
        else if (this == Piggyship) onPiggyship();
        else if (this == Logoix) onLogoix();
        else if (this == KolayGelsin) onKolayGelsin();
        else if (this == AssociatedCouriers) onAssociatedCouriers();
        else if (this == UpsChecker) onUpsChecker();
        else if (this == Wineshipping) onWineshipping();
        else if (this == Spedisci) onSpedisci();
        else if (this == Fourkites) onFourkites();
        else if (this == Etonas) onEtonas();
        else if (this == Finmile) onFinmile();
        else if (this == Uniuni) onUniuni();
        else if (this == Rodonaves) onRodonaves();
        else if (this == InpostIt) onInpostIt();
        else if (this == TforceFreight) onTforceFreight();
        else if (this == Richmom) onRichmom();
        else if (this == Franco) onFranco();
        else if (this == Ecparcel) onEcparcel();
        else if (this == FedexChina) onFedexChina();
        else if (this == GofoExpress) onGofoExpress();
        else if (this == Shipbob) onShipbob();
        else if (this == JerseypostAtlas) onJerseypostAtlas();
        else if (this == Coretrails) onCoretrails();
        else if (this == RhenusItaly) onRhenusItaly();
        else if (this == Jadlog) onJadlog();
        else if (this == Jitsu) onJitsu();
        else if (this == YanwenExpress) onYanwenExpress();
        else if (this == Dashlink) onDashlink();
        else if (this == SeinoSuperExpress) onSeinoSuperExpress();
        else if (this == Floship) onFloship();
        else if (this == Metroscg) onMetroscg();
        else if (this == Sendparcel) onSendparcel();
        else if (this == P2P) onP2P();
        else if (this == CnExpress) onCnExpress();
        else if (this == Cirrotrack) onCirrotrack();
        else if (this == LandLogistics) onLandLogistics();
        else if (this == Veho) onVeho();
        else if (this == Medline) onMedline();
        else if (this == Vdtrack) onVdtrack();
        else if (this == SinoScm) onSinoScm();
        else if (this == _3PeExpress) on_3PeExpress();
        else if (this == Swiftx) onSwiftx();
        else if (this == Sfydexpress) onSfydexpress();
        else if (this == Toptrans) onToptrans();
        else if (this == Other) onOther();
        else otherwise(Value);
    }
}

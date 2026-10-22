// TREEITEMTYPES per Beckhoff Infosys "tc3_automationinterface/242781195".
// Member values (Action/Method/Property/Get/Set) were verified empirically
// against the live XAE on 2026-05-15 (feature/pou-handling spike): a PROPERTY
// is created bare (611) and its Get/Set accessors are *separate* CreateChild
// calls (613/614) — the earlier "612 = accessor" guess was wrong.

namespace PtcXaeMcp.WinForms.TwinCat;

internal static class PlcItemType
{
    public const int PlcApp           = 600;  // PLC source-project root; valid GVL/DUT container.
    public const int Folder           = 601;
    public const int PouProgram       = 602;
    public const int PouFunction      = 603;
    public const int PouFunctionBlock = 604;

    // DUT TREEITEMTYPES are kind-specific (verified live 2026-06-03 against
    // 100001: struct DUTs enumerate as 606 and were silently dropped by the
    // old "Dut=605 only" classifier; enums are 605). Creation passes the
    // matching nSubType to CreateChild so TwinCAT builds the right skeleton.
    public const int Dut              = 605;  // PLCDUTENUM (the enum DUT kind).
    public const int DutStruct        = 606;  // PLCDUTSTRUCT
    public const int DutUnion         = 607;  // PLCDUTUNION
    public const int DutAlias         = 623;  // PLCDUTALIAS

    public const int Gvl              = 615;
    // 629 = PLCGVLPARAMLIST, a Parameter List (TREEITEMTYPES in TCatSysManagerLib; Infosys "PLC-Objekte
    // anlegen": "Parameter List", category PL). It is NOT a persistent-variables list — this constant
    // was named GvlPersistent until fix/gvl-persistent-629 (live 2026-10-06: CreateChild(…, 629) gave
    // the skeleton `VAR_GLOBAL CONSTANT` and a saved file with ParameterList="True"). Persistent
    // variables are an ordinary GVL (615) with a `VAR_GLOBAL PERSISTENT` section. Kept only so existing
    // parameter lists stay readable, writable and deletable; never created by this server.
    public const int GvlParamList     = 629;

    // PLC INTERFACE object ("PLCITF"). Per the Infosys 242781195 item-type table the
    // INTERFACE object is 618 (617=PLCLIBMAN, the References/library-manager node
    // already used as ItemType_References in TwinCatComSession.Libraries.cs; 619=PLCVISOBJ).
    // It is created via CreateChild like a declaration-only DUT/GVL with a null vInfo;
    // only its METHODS (PLCITFMETH) carry the ConsumeXml quirk and are out of scope here.
    // nSubType 618 is empirically unconfirmed on a live CreateChild — verified on the
    // first live create_interface run; if 618 is rejected, the CreateChild failure
    // message names the rejected code.
    public const int Interface        = 618;  // PLCITF

    // Structured POU members (feature/pou-handling, verified 2026-05-15).
    public const int Action           = 608;
    public const int Method           = 609;
    public const int Property         = 611;
    public const int PropertyGet      = 613;
    public const int PropertySet      = 614;

    // Interface members use DISTINCT nSubTypes from POU members (Infosys 242732427
    // "Creating and editing PLC objects"): an interface METHOD is 610 (not the POU
    // method 609) with vInfo[0]=return type; interface PROPERTY 612; interface
    // property accessors GET 654 / SET 655 (null vInfo). The POU equivalents are
    // 609/611/613/614 with a language vInfo. Same kind-specific-subtype pattern as
    // the DUT subtypes (STRUCT 606 / ENUM 605 / …). nSubTypes documented; the live
    // CreateChild shape is empirically gated by the interface-members integration
    // test — if a code is rejected, the CreateChild failure message names it.
    public const int InterfaceMethod   = 610;
    public const int InterfaceProperty = 612;
    public const int InterfaceGet      = 654;
    public const int InterfaceSet      = 655;

    // Remaining TREEITEMTYPES 600-699 (TCatSysManagerLib, read by reflection 2026-10-06; feature/plc-objects).
    // Not touched by the old tools; the plc_* element catalog (PlcElementCatalog) names every one.
    public const int Transition        = 616;  // PLCTRANSITION
    public const int LibMan            = 617;  // PLCLIBMAN (References node)
    public const int VisObj            = 619;  // PLCVISOBJ
    public const int VisMan            = 620;  // PLCVISMAN
    public const int Task              = 621;  // PLCTASK
    public const int ProjectInfo       = 622;  // PLCPROJECTINFO
    public const int TargetVisu        = 624;  // PLCTARGETVISU
    public const int GlobalTextList    = 625;  // PLCGLOBALTEXTLIST
    public const int TextList          = 626;  // PLCTEXTLIST
    public const int GlobalImagePool   = 627;  // PLCGLOBALIMAGEPOOL
    public const int ImagePool         = 628;  // PLCIMAGEPOOL
    public const int WebVisu           = 630;  // PLCWEBVISU
    public const int UmlClassDiagram   = 631;  // PLCUMLCLASSDIAGRAM
    public const int RecipeManager     = 632;  // PLCRECIPEMANAGER
    public const int RecipeDefinition  = 633;  // PLCRECIPEDEFINITION
    public const int TrendObject       = 634;  // PLCTRENDOBJECT
    public const int TrendManager      = 635;  // PLCTRENDMANAGER
    public const int ProgRef           = 650;  // PLCPROGREF
    public const int ExtDataType       = 651;  // PLCEXTDATATYPE
    public const int ExtDataTypeCont   = 652;  // PLCEXTDATATYPECONT
    public const int TmcDescription    = 653;  // PLCTMCDESCRIPTION
    public const int LibRef            = 656;  // PLCLIBREF
    public const int LibPlaceholder    = 657;  // PLCLIBPLACEHOLDER
    public const int TextListEnumeration = 658; // PLCTEXTLISTENUMERATION
}

// Implementation-language codes used as vInfo[0] for CreateChild on POUs.
// Empty/omitted input defaults to ST. An UNKNOWN name is an error (issue #148, B-8:
// implLanguage="FDB" used to become ST silently); callers validate with Validate()
// before touching the session, and ToCode throws as a last line of defence.
internal static class PlcImplLanguage
{
    public const int St  = 1;
    public const int Il  = 2;
    public const int Sfc = 3;
    public const int Fbd = 4;
    public const int Cfc = 5;
    public const int Ld  = 6;

    public const string Expected = "ST, IL, SFC, FBD, CFC or LD";

    public static bool TryToCode(string? language, out int code)
    {
        code = St;
        if (string.IsNullOrWhiteSpace(language)) return true;
        int? c = language.Trim().ToUpperInvariant() switch
        {
            "ST"  => St,
            "IL"  => Il,
            "SFC" => Sfc,
            "FBD" => Fbd,
            "CFC" => Cfc,
            "LD"  => Ld,
            _     => null,
        };
        if (c is null) return false;
        code = c.Value;
        return true;
    }

    /// <summary>Null when <paramref name="language"/> is empty or known, else an "[Error] ..." string.</summary>
    public static string? Validate(string? language) =>
        TryToCode(language, out _)
            ? null
            : $"[Error] Unknown implLanguage '{language}'. Expected {Expected}.";

    /// <summary>Canonical upper-case name ("ST" when omitted). Call only after <see cref="Validate"/>.</summary>
    public static string Normalize(string? language) =>
        string.IsNullOrWhiteSpace(language) ? "ST" : language.Trim().ToUpperInvariant();

    /// <summary>SFC, FBD, CFC and LD keep their implementation as XAE-native XML, not as text (live 2026-10-07).</summary>
    public static bool IsGraphical(string? language) =>
        language is not null && language.Trim().ToUpperInvariant() is "SFC" or "FBD" or "CFC" or "LD";

    /// <summary>Root element of the XML implementation of a graphical language (LD and FBD share the network list).</summary>
    public static string? XmlRoot(string? language) => language?.Trim().ToUpperInvariant() switch
    {
        "LD" or "FBD" => "NWL",
        "SFC" => "SFC",
        "CFC" => "CFC",
        _ => null,
    };

    /// <summary>
    /// Null when <paramref name="implementation"/> is acceptable for a POU written in the graphical
    /// <paramref name="language"/>: well-formed XML whose root element is that language's (XAE refuses anything else,
    /// but only after the logout). Otherwise the reason, without the "[Error] " prefix.
    /// </summary>
    public static string? CheckGraphicalXml(string language, string implementation)
    {
        string root = XmlRoot(language)!;
        try
        {
            var doc = System.Xml.Linq.XDocument.Parse(implementation);
            return doc.Root?.Name.LocalName == root
                ? null
                : $"the XML root is <{doc.Root?.Name.LocalName}>, a {language.ToUpperInvariant()} implementation has <{root}>";
        }
        catch (System.Xml.XmlException ex)
        {
            return $"a {language.ToUpperInvariant()} implementation is XAE's XML (<{root}>…, as returned by twincat_plc_read), " +
                   $"the given text is not: {ex.Message}";
        }
    }

    public static int ToCode(string? language) =>
        TryToCode(language, out int code)
            ? code
            : throw new ArgumentException($"Unknown implLanguage '{language}'. Expected {Expected}.");

    public static string FromCode(int code) => code switch
    {
        St  => "ST",
        Il  => "IL",
        Sfc => "SFC",
        Fbd => "FBD",
        Cfc => "CFC",
        Ld  => "LD",
        _   => "ST",
    };
}

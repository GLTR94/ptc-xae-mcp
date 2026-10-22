using System.Text.RegularExpressions;

namespace PtcXaeMcp.WinForms.TwinCat;

// Fast-fail pre-check for PLC object names (POUs, methods, actions,
// properties, rename targets). It catches the COMMON mistakes — bad syntax and
// the reserved words that TC3 reports with the misleading "...not possible on
// node (Name mismatch)" / build error C0243 (issue #19: e.g. CALC, ADD are IL
// operators). It is deliberately NOT exhaustive: the full TwinCAT/IEC reserved
// set is large and version-dependent, so TC3 itself stays the authority — the
// session still catches and explains a "Name mismatch" that slips past here.
internal static partial class IecIdentifier
{
    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex SyntaxRx();

    // Definite conflicts only: IL operators, ST/structuring keywords, and the
    // elementary data types. Standard library FUNCTION/FB names (ABS, TON, …)
    // are intentionally omitted — they are usually shadowable and listing them
    // risks rejecting valid names; TC3 is the backstop for those.
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        // IL operators (the issue-#19 culprits)
        "LD","LDN","ST","STN","S","R","AND","ANDN","OR","ORN","XOR","XORN","NOT",
        "ADD","SUB","MUL","DIV","MOD","GT","GE","EQ","NE","LE","LT",
        "JMP","JMPC","JMPCN","CAL","CALC","CALCN","RET","RETC","RETCN",
        // ST / structuring keywords
        "IF","THEN","ELSE","ELSIF","END_IF","CASE","OF","END_CASE",
        "FOR","TO","BY","DO","END_FOR","WHILE","END_WHILE","REPEAT","UNTIL",
        "END_REPEAT","EXIT","CONTINUE","RETURN","JMP_",
        "VAR","VAR_INPUT","VAR_OUTPUT","VAR_IN_OUT","VAR_GLOBAL","VAR_TEMP",
        "VAR_EXTERNAL","VAR_ACCESS","VAR_CONFIG","VAR_STAT","END_VAR",
        "CONSTANT","RETAIN","PERSISTENT","AT","ABSTRACT","FINAL","PUBLIC",
        "PRIVATE","PROTECTED","INTERNAL",
        "TYPE","END_TYPE","STRUCT","END_STRUCT","UNION","END_UNION","ARRAY",
        "FUNCTION","END_FUNCTION","FUNCTION_BLOCK","END_FUNCTION_BLOCK",
        "PROGRAM","END_PROGRAM","METHOD","END_METHOD","PROPERTY","END_PROPERTY",
        "ACTION","END_ACTION","STEP","INITIAL_STEP","END_STEP",
        "TRANSITION","END_TRANSITION","INTERFACE","END_INTERFACE",
        "EXTENDS","IMPLEMENTS","THIS","SUPER","__SYSTEM",
        "TRUE","FALSE","NULL",
        // Elementary data types
        "BOOL","BYTE","WORD","DWORD","LWORD","SINT","INT","DINT","LINT",
        "USINT","UINT","UDINT","ULINT","REAL","LREAL",
        "TIME","LTIME","DATE","TIME_OF_DAY","TOD","DATE_AND_TIME","DT",
        "STRING","WSTRING","CHAR","WCHAR","POINTER","REFERENCE","ANY",
        "ANY_BIT","ANY_NUM","ANY_INT","ANY_REAL","ANY_DATE",
    };

    /// <summary>
    /// Returns an "[Error] …" message if <paramref name="name"/> is not a
    /// valid, non-reserved IEC 61131-3 identifier; otherwise null.
    /// <paramref name="what"/> labels the thing being named (e.g. "METHOD name").
    /// </summary>
    public static string? Validate(string? name, string what)
    {
        if (string.IsNullOrWhiteSpace(name))
            return $"[Error] {what} is required.";
        string n = name.Trim();
        if (!SyntaxRx().IsMatch(n))
            return $"[Error] {what} '{name}' is not a valid IEC 61131-3 identifier " +
                   "(letters, digits, underscore; must not start with a digit).";
        if (Reserved.Contains(n))
            return $"[Error] {what} '{name}' is a reserved IEC 61131-3 word " +
                   "(keyword, IL operator such as ADD/CALC, or elementary type) " +
                   "and cannot be used as an object name. Choose a different name.";
        return null;
    }
}

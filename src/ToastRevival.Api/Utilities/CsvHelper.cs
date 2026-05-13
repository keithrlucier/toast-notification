namespace ToastRevival.Api.Utilities;

public static class CsvHelper
{
    /// <summary>
    /// Encodes <paramref name="value"/> as a single CSV cell with two
    /// defenses applied:
    ///
    /// 1. Standard CSV quoting — when the value contains a comma, quote, or
    ///    newline, wrap in double quotes and escape interior quotes by
    ///    doubling them. (RFC 4180.)
    /// 2. Formula-injection neutralization — when the value starts with
    ///    <c>=</c>, <c>+</c>, <c>-</c>, <c>@</c>,
    ///    <c>\t</c>, or <c>\r</c>, prefix with a single apostrophe. Excel,
    ///    LibreOffice Calc, and Google Sheets all honor a leading
    ///    apostrophe as the "treat this cell as literal text" sentinel and
    ///    strip it from the rendered display. Without this prefix, an audit
    ///    log row whose Action or ResourceId began with <c>=CMD()</c> would
    ///    execute as a formula when an admin opened the export in Excel.
    /// </summary>
    public static string Cell(string value)
    {
        if (!string.IsNullOrEmpty(value) && IsFormulaTrigger(value[0]))
            value = "'" + value;

        return value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }

    private static bool IsFormulaTrigger(char c) =>
        c == '=' || c == '+' || c == '-' || c == '@' || c == '\t' || c == '\r';
}

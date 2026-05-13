using ToastRevival.Api.Utilities;
using Xunit;

namespace ToastRevival.Api.Tests;

/// <summary>
/// Unit tests for <see cref="CsvHelper"/>. Pure-function utility; no DB,
/// fixture, or HTTP wiring needed — runs in milliseconds.
///
/// Covers CSV formula injection in audit / delivery exports. Excel /
/// LibreOffice / Google Sheets treat cells starting with =, +, -, @ as
/// formulas. A controlled string in the audit log Action or ResourceId could
/// trigger formula execution when an admin opens the export.
/// </summary>
public sealed class CsvHelperTests
{
    [Theory]
    [InlineData("=cmd|'/c calc'!A1",  "'=cmd|'/c calc'!A1")]
    [InlineData("+1+1",                "'+1+1")]
    [InlineData("-2+3",                "'-2+3")]
    [InlineData("@SUM(A1:A2)",         "'@SUM(A1:A2)")]
    [InlineData("\t=DANGER",          "'\t=DANGER")]
    [InlineData("\r=DANGER",          "'\r=DANGER")]
    public void Cell_PrefixesFormulaTriggerWithApostrophe(string input, string expected)
    {
        Assert.Equal(expected, CsvHelper.Cell(input));
    }

    [Theory]
    [InlineData("",                    "")]
    [InlineData("notification.send",   "notification.send")]
    [InlineData("Toast Notification",  "Toast Notification")]
    [InlineData("123",                 "123")]
    [InlineData("user@example.com",    "user@example.com")]
    public void Cell_LeavesSafeValuesUnchanged(string input, string expected)
    {
        Assert.Equal(expected, CsvHelper.Cell(input));
    }

    [Fact]
    public void Cell_QuotesAndEscapesValueContainingComma()
    {
        Assert.Equal("\"a,b\"", CsvHelper.Cell("a,b"));
    }

    [Fact]
    public void Cell_QuotesAndEscapesValueContainingDoubleQuote()
    {
        Assert.Equal("\"a\"\"b\"", CsvHelper.Cell("a\"b"));
    }

    [Fact]
    public void Cell_QuotesValueContainingNewline()
    {
        Assert.Equal("\"line1\nline2\"", CsvHelper.Cell("line1\nline2"));
    }

    [Fact]
    public void Cell_StacksFormulaPrefixUnderQuoting()
    {
        // Value triggers BOTH defenses: starts with @ (formula trigger) AND
        // contains comma (quoting trigger). The apostrophe goes inside the
        // outer double quotes so Excel still strips it on render.
        Assert.Equal("\"'@SUM(A1,A2)\"", CsvHelper.Cell("@SUM(A1,A2)"));
    }
}

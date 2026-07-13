using System.Text;

using VerbatimIntelligence.Api.Uploads;

namespace VerbatimIntelligence.Api.Tests;

/// <summary>
/// The CSV contract of docs/v1-spec.md, one test per rule. Pure parsing, no
/// infrastructure — the rejection rules are the risky surface, so they get the
/// coverage.
/// </summary>
public sealed class CsvContractTests
{
    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    private static CsvParseResult.ParsedCsv ParseOk(byte[] content)
    {
        var result = CsvContract.Parse(content);
        return Assert.IsType<CsvParseResult.ParsedCsv>(result);
    }

    private static string RejectMessage(byte[] content)
    {
        var result = CsvContract.Parse(content);
        return Assert.IsType<CsvParseResult.RejectedCsv>(result).Message;
    }

    [Fact]
    public void Parses_CommaDelimited_HeadersAndRows()
    {
        var parsed = ParseOk(Utf8("comment,score\nGreat product,9\nToo slow,3\n"));

        Assert.Equal(["comment", "score"], parsed.Headers);
        Assert.Equal(2, parsed.Rows.Count);
        Assert.Equal(["Great product", "9"], parsed.Rows[0]);
        Assert.Equal(["Too slow", "3"], parsed.Rows[1]);
    }

    [Fact]
    public void DetectsSemicolonDelimiter_FromHeaderLine()
    {
        var parsed = ParseOk(Utf8("comment;score\nhello, world;5\n"));

        Assert.Equal(["comment", "score"], parsed.Headers);
        Assert.Equal(["hello, world", "5"], parsed.Rows[0]);
    }

    [Fact]
    public void ToleratesUtf8Bom()
    {
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. Utf8("verbatim\nfoo\n")];

        var parsed = ParseOk(bytes);

        Assert.Equal(["verbatim"], parsed.Headers);
        Assert.Equal(["foo"], parsed.Rows[0]);
    }

    [Fact]
    public void KeepsQuotedDelimitersInsideCells()
    {
        var parsed = ParseOk(Utf8("comment,score\n\"a, b, c\",5\n"));

        Assert.Equal(["a, b, c", "5"], parsed.Rows[0]);
    }

    [Fact]
    public void SingleColumnFile_IsOneColumn()
    {
        var parsed = ParseOk(Utf8("verbatim\nI love it\nI hate it\n"));

        Assert.Equal(["verbatim"], parsed.Headers);
        Assert.Equal(2, parsed.Rows.Count);
    }

    [Fact]
    public void HeaderOnly_IsValidWithNoRows()
    {
        var parsed = ParseOk(Utf8("verbatim,score\n"));

        Assert.Equal(["verbatim", "score"], parsed.Headers);
        Assert.Empty(parsed.Rows);
    }

    [Fact]
    public void RejectsEmptyFile()
    {
        Assert.Contains("empty", RejectMessage([]), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsWhitespaceOnlyFile()
    {
        Assert.Contains("empty", RejectMessage(Utf8("   \n  \n")), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsNonUtf8Bytes()
    {
        // 0xC3 opens a 2-byte sequence that 0x28 does not continue.
        Assert.Contains("UTF-8", RejectMessage([0xC3, 0x28]), StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsFileLargerThanFiveMegabytes()
    {
        var oversized = new byte[CsvContract.MaxBytes + 1];
        Array.Fill(oversized, (byte)'a');

        Assert.Contains("5 MB", RejectMessage(oversized), StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsMoreThanMaxDataRows()
    {
        var builder = new StringBuilder("verbatim\n");
        for (var i = 0; i < CsvContract.MaxDataRows + 1; i++)
        {
            builder.Append("row\n");
        }

        Assert.Contains("data rows", RejectMessage(Utf8(builder.ToString())), StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsCellLongerThanLimit()
    {
        var huge = new string('x', CsvContract.MaxCellChars + 1);

        Assert.Contains("character limit", RejectMessage(Utf8($"verbatim\n{huge}\n")), StringComparison.Ordinal);
    }
}
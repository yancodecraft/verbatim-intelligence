using System.Globalization;
using System.Text;

using CsvHelper;
using CsvHelper.Configuration;

namespace VerbatimIntelligence.Api.Uploads;

/// <summary>
/// The CSV ingestion contract of the V1 spec (see docs/v1-spec.md): UTF-8 with
/// an optional BOM, delimiter auto-detected between ',' and ';', hard limits on
/// size, row count and cell length, a required header row. Every violation is a
/// clear, actionable message — never an exception bubbling up as a 500.
/// </summary>
public static class CsvContract
{
    public const int MaxBytes = 5 * 1024 * 1024;
    public const int MaxDataRows = 5000;
    public const int MaxCellChars = 10_000;

    // Strict decoder: invalid byte sequences throw rather than yielding
    // replacement characters, which is how a binary file is told from CSV.
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Parses and validates raw file bytes against the contract. Returns the
    /// detected columns and data rows, or a rejection carrying its message.
    /// </summary>
    public static CsvParseResult Parse(byte[] content)
    {
        if (content.Length == 0)
        {
            return CsvParseResult.Reject("The file is empty.");
        }

        if (content.Length > MaxBytes)
        {
            return CsvParseResult.Reject("The file is larger than the 5 MB limit.");
        }

        string text;
        try
        {
            text = StrictUtf8.GetString(content);
        }
        catch (DecoderFallbackException)
        {
            return CsvParseResult.Reject("The file is not valid UTF-8 text — is it really a CSV file?");
        }

        // Tolerate a leading BOM (the strict decoder keeps it as U+FEFF).
        text = text.TrimStart('﻿');
        if (text.Trim().Length == 0)
        {
            return CsvParseResult.Reject("The file is empty.");
        }

        var delimiter = DetectDelimiter(text);
        var configuration = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = delimiter.ToString(),
            HasHeaderRecord = false,
            // Untrusted input: tolerate stray quotes rather than throwing.
            BadDataFound = null,
            MissingFieldFound = null,
        };

        using var csv = new CsvReader(new StringReader(text), configuration);

        if (!csv.Read())
        {
            return CsvParseResult.Reject("The first row must contain column headers.");
        }

        var headers = ReadRow(csv);
        if (headers.Count == 0 || headers.All(string.IsNullOrWhiteSpace))
        {
            return CsvParseResult.Reject("The first row must contain column headers.");
        }

        if (headers.Any(header => header.Length > MaxCellChars))
        {
            return CsvParseResult.Reject($"A cell exceeds the {MaxCellChars}-character limit.");
        }

        var rows = new List<IReadOnlyList<string>>();
        while (csv.Read())
        {
            var row = ReadRow(csv);
            if (row.Any(cell => cell.Length > MaxCellChars))
            {
                return CsvParseResult.Reject($"A cell exceeds the {MaxCellChars}-character limit.");
            }

            rows.Add(row);
            if (rows.Count > MaxDataRows)
            {
                return CsvParseResult.Reject($"The file has more than {MaxDataRows} data rows.");
            }
        }

        return CsvParseResult.Parsed(headers, rows);
    }

    // The header line decides: whichever of ';' or ',' appears more often wins,
    // ties (and comma-only) default to ',' (see docs/v1-spec.md).
    private static char DetectDelimiter(string text)
    {
        var firstLineEnd = text.IndexOfAny(['\r', '\n']);
        var headerLine = firstLineEnd < 0 ? text : text[..firstLineEnd];
        var semicolons = headerLine.Count(character => character == ';');
        var commas = headerLine.Count(character => character == ',');
        return semicolons > commas ? ';' : ',';
    }

    private static List<string> ReadRow(CsvReader csv)
    {
        var row = new List<string>(csv.Parser.Count);
        for (var i = 0; i < csv.Parser.Count; i++)
        {
            row.Add(csv.GetField(i) ?? "");
        }

        return row;
    }
}

/// <summary>Outcome of a parse: either the parsed contents or a rejection.</summary>
public abstract record CsvParseResult
{
    public static CsvParseResult Parsed(
        IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows) =>
        new ParsedCsv(headers, rows);

    public static CsvParseResult Reject(string message) => new RejectedCsv(message);

    public sealed record ParsedCsv(
        IReadOnlyList<string> Headers,
        IReadOnlyList<IReadOnlyList<string>> Rows) : CsvParseResult;

    public sealed record RejectedCsv(string Message) : CsvParseResult;
}
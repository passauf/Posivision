using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;

/// <summary>
/// Klinik tablo dışa aktarımı: gerçek .csv (UTF-8 BOM) + .xlsx (Excel OOXML).
/// Hot path değil — seans/ilerleme export'ta heap kullanımı kabul edilir.
/// SaMD Class B / KVKK: içerik PatientVault üzerinden yerelde saklanır.
/// </summary>
public static class ReportSpreadsheetWriter
{
    private static readonly UTF8Encoding Utf8Bom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>
    /// Aynı içerikten .csv ve .xlsx yazar (ayrı klasörlere).
    /// </summary>
    public static void WriteCsvAndXlsx(
        string csvDirectory,
        string excelDirectory,
        string baseFileNameWithoutExtension,
        string sheetName,
        string[] headers,
        List<string[]> rows)
    {
        if (string.IsNullOrEmpty(baseFileNameWithoutExtension)) return;
        if (headers == null || headers.Length == 0) return;

        if (!string.IsNullOrEmpty(csvDirectory))
        {
            string csvText = BuildCsv(headers, rows);
            PatientVault.WriteEncrypted(csvDirectory, baseFileNameWithoutExtension + ".csv", csvText);
        }

        if (!string.IsNullOrEmpty(excelDirectory))
        {
            byte[] xlsxBytes = BuildXlsx(sheetName ?? "Sheet1", headers, rows);
            if (xlsxBytes != null && xlsxBytes.Length > 0)
                PatientVault.WriteEncryptedBytes(excelDirectory, baseFileNameWithoutExtension + ".xlsx", xlsxBytes);
        }
    }

    /// <summary>Geriye uyumluluk — aynı klasöre yazar.</summary>
    public static void WriteCsvAndXlsx(
        string directory,
        string baseFileNameWithoutExtension,
        string sheetName,
        string[] headers,
        List<string[]> rows)
    {
        WriteCsvAndXlsx(directory, directory, baseFileNameWithoutExtension, sheetName, headers, rows);
    }

    /// <summary>İki sayfalı Excel + tek CSV (özet bloğu + seans tablosu).</summary>
    public static void WriteProgressCsvAndXlsx(
        string csvDirectory,
        string excelDirectory,
        string baseFileNameWithoutExtension,
        string[] summaryHeaders,
        List<string[]> summaryRows,
        string[] sessionHeaders,
        List<string[]> sessionRows)
    {
        if (string.IsNullOrEmpty(baseFileNameWithoutExtension))
            return;

        var csv = new StringBuilder(8192);
        if (summaryHeaders != null && summaryRows != null && summaryRows.Count > 0)
        {
            AppendCsvTable(csv, summaryHeaders, summaryRows);
            csv.Append('\n');
        }
        if (sessionHeaders != null)
            AppendCsvTable(csv, sessionHeaders, sessionRows);

        if (!string.IsNullOrEmpty(csvDirectory))
            PatientVault.WriteEncrypted(csvDirectory, baseFileNameWithoutExtension + ".csv", csv.ToString());

        var sheets = new List<SheetSpec>(2);
        if (sessionHeaders != null && sessionHeaders.Length > 0)
            sheets.Add(new SheetSpec("Seanslar", sessionHeaders, sessionRows));
        if (summaryHeaders != null && summaryHeaders.Length > 0 && summaryRows != null && summaryRows.Count > 0)
            sheets.Add(new SheetSpec("Ozet", summaryHeaders, summaryRows));

        if (sheets.Count == 0 || string.IsNullOrEmpty(excelDirectory)) return;
        byte[] xlsx = BuildXlsxMulti(sheets);
        if (xlsx != null && xlsx.Length > 0)
            PatientVault.WriteEncryptedBytes(excelDirectory, baseFileNameWithoutExtension + ".xlsx", xlsx);
    }

    /// <summary>Geriye uyumluluk — aynı klasöre yazar.</summary>
    public static void WriteProgressCsvAndXlsx(
        string directory,
        string baseFileNameWithoutExtension,
        string[] summaryHeaders,
        List<string[]> summaryRows,
        string[] sessionHeaders,
        List<string[]> sessionRows)
    {
        WriteProgressCsvAndXlsx(
            directory, directory, baseFileNameWithoutExtension,
            summaryHeaders, summaryRows, sessionHeaders, sessionRows);
    }

    public static string BuildCsv(string[] headers, List<string[]> rows)
    {
        var sb = new StringBuilder(4096);
        AppendCsvTable(sb, headers, rows);
        return sb.ToString();
    }

    private static void AppendCsvTable(StringBuilder sb, string[] headers, List<string[]> rows)
    {
        if (sb == null || headers == null) return;
        for (int i = 0; i < headers.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(CsvEscape(headers[i]));
        }
        sb.Append('\n');

        if (rows == null) return;
        for (int r = 0; r < rows.Count; r++)
        {
            string[] row = rows[r];
            if (row == null) continue;
            int n = Mathf.Min(headers.Length, row.Length);
            for (int c = 0; c < headers.Length; c++)
            {
                if (c > 0) sb.Append(',');
                if (c < n) sb.Append(CsvEscape(row[c]));
            }
            sb.Append('\n');
        }
    }

    private static string CsvEscape(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        bool need = s.IndexOf(',') >= 0 || s.IndexOf('"') >= 0 || s.IndexOf('\n') >= 0 || s.IndexOf('\r') >= 0;
        if (!need) return s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }

    private struct SheetSpec
    {
        public string name;
        public string[] headers;
        public List<string[]> rows;

        public SheetSpec(string name, string[] headers, List<string[]> rows)
        {
            this.name = name;
            this.headers = headers;
            this.rows = rows;
        }
    }

    public static byte[] BuildXlsx(string sheetName, string[] headers, List<string[]> rows)
    {
        var sheets = new List<SheetSpec>(1)
        {
            new SheetSpec(sheetName, headers, rows)
        };
        return BuildXlsxMulti(sheets);
    }

    private static byte[] BuildXlsxMulti(List<SheetSpec> sheets)
    {
        if (sheets == null || sheets.Count == 0) return null;

        try
        {
            using (var ms = new MemoryStream())
            {
                using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
                {
                    WriteZipEntry(zip, "[Content_Types].xml", BuildContentTypes(sheets.Count));
                    WriteZipEntry(zip, "_rels/.rels", BuildRootRels());
                    WriteZipEntry(zip, "xl/workbook.xml", BuildWorkbook(sheets));
                    WriteZipEntry(zip, "xl/_rels/workbook.xml.rels", BuildWorkbookRels(sheets.Count));
                    WriteZipEntry(zip, "xl/styles.xml", BuildStyles());

                    for (int i = 0; i < sheets.Count; i++)
                    {
                        SheetSpec sh = sheets[i];
                        string path = "xl/worksheets/sheet" + (i + 1).ToString(Inv) + ".xml";
                        WriteZipEntry(zip, path, BuildSheetXml(sh.headers, sh.rows));
                    }
                }
                return ms.ToArray();
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void WriteZipEntry(ZipArchive zip, string entryName, string utf8Xml)
    {
        ZipArchiveEntry entry = zip.CreateEntry(entryName, System.IO.Compression.CompressionLevel.Optimal);
        using (var stream = entry.Open())
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            writer.Write(utf8Xml);
    }

    private static string BuildContentTypes(int sheetCount)
    {
        var sb = new StringBuilder(512);
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
        sb.Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>");
        sb.Append("<Default Extension=\"xml\" ContentType=\"application/xml\"/>");
        sb.Append("<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>");
        sb.Append("<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");
        for (int i = 1; i <= sheetCount; i++)
        {
            sb.Append("<Override PartName=\"/xl/worksheets/sheet").Append(i.ToString(Inv))
              .Append(".xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
        }
        sb.Append("</Types>");
        return sb.ToString();
    }

    private static string BuildRootRels()
    {
        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
            + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
            + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>"
            + "</Relationships>";
    }

    private static string BuildWorkbook(List<SheetSpec> sheets)
    {
        var sb = new StringBuilder(512);
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append("<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" ");
        sb.Append("xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">");
        sb.Append("<sheets>");
        for (int i = 0; i < sheets.Count; i++)
        {
            string name = SanitizeSheetName(sheets[i].name);
            sb.Append("<sheet name=\"").Append(XmlEscapeAttr(name)).Append("\" sheetId=\"")
              .Append((i + 1).ToString(Inv)).Append("\" r:id=\"rId").Append((i + 1).ToString(Inv)).Append("\"/>");
        }
        sb.Append("</sheets></workbook>");
        return sb.ToString();
    }

    private static string BuildWorkbookRels(int sheetCount)
    {
        var sb = new StringBuilder(512);
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
        for (int i = 1; i <= sheetCount; i++)
        {
            sb.Append("<Relationship Id=\"rId").Append(i.ToString(Inv))
              .Append("\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet")
              .Append(i.ToString(Inv)).Append(".xml\"/>");
        }
        sb.Append("<Relationship Id=\"rId").Append((sheetCount + 1).ToString(Inv))
          .Append("\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>");
        sb.Append("</Relationships>");
        return sb.ToString();
    }

    private static string BuildStyles()
    {
        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
            + "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">"
            + "<fonts count=\"1\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>"
            + "<fills count=\"1\"><fill><patternFill patternType=\"none\"/></fill></fills>"
            + "<borders count=\"1\"><border/></borders>"
            + "<cellStyleXfs count=\"1\"><xf/></cellStyleXfs>"
            + "<cellXfs count=\"1\"><xf xfId=\"0\"/></cellXfs>"
            + "</styleSheet>";
    }

    private static string BuildSheetXml(string[] headers, List<string[]> rows)
    {
        var sb = new StringBuilder(8192);
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
        sb.Append("<sheetData>");

        int rowIndex = 1;
        AppendXlsxRow(sb, rowIndex++, headers);

        if (rows != null)
        {
            for (int r = 0; r < rows.Count; r++)
            {
                string[] row = rows[r];
                if (row == null) continue;
                // Sütun sayısı başlıkla hizala
                if (headers != null && row.Length != headers.Length)
                {
                    var padded = new string[headers.Length];
                    for (int c = 0; c < headers.Length; c++)
                        padded[c] = c < row.Length ? row[c] : "";
                    AppendXlsxRow(sb, rowIndex++, padded);
                }
                else
                {
                    AppendXlsxRow(sb, rowIndex++, row);
                }
            }
        }

        sb.Append("</sheetData></worksheet>");
        return sb.ToString();
    }

    private static void AppendXlsxRow(StringBuilder sb, int rowIndex1Based, string[] cells)
    {
        if (cells == null) return;
        sb.Append("<row r=\"").Append(rowIndex1Based.ToString(Inv)).Append("\">");
        for (int c = 0; c < cells.Length; c++)
        {
            string refId = ColumnName(c) + rowIndex1Based.ToString(Inv);
            string val = cells[c] ?? "";
            if (TryParseNumber(val, out string numLiteral))
            {
                sb.Append("<c r=\"").Append(refId).Append("\"><v>")
                  .Append(numLiteral).Append("</v></c>");
            }
            else
            {
                sb.Append("<c r=\"").Append(refId).Append("\" t=\"inlineStr\"><is><t>")
                  .Append(XmlEscapeText(val)).Append("</t></is></c>");
            }
        }
        sb.Append("</row>");
    }

    private static bool TryParseNumber(string s, out string literal)
    {
        literal = null;
        if (string.IsNullOrEmpty(s)) return false;
        // Yüzde / derece içeren metinler string kalsın
        for (int i = 0; i < s.Length; i++)
        {
            char ch = s[i];
            if (ch == '%' || ch == '°' || ch == '/') return false;
        }
        if (double.TryParse(s, NumberStyles.Float, Inv, out double d)
            && !double.IsNaN(d) && !double.IsInfinity(d))
        {
            literal = d.ToString("G15", Inv);
            return true;
        }
        return false;
    }

    private static string ColumnName(int zeroBased)
    {
        // 0 -> A, 25 -> Z, 26 -> AA
        int n = zeroBased + 1;
        char[] buf = new char[8];
        int len = 0;
        while (n > 0)
        {
            int rem = (n - 1) % 26;
            buf[len++] = (char)('A' + rem);
            n = (n - 1) / 26;
        }
        for (int i = 0; i < len / 2; i++)
        {
            char t = buf[i];
            buf[i] = buf[len - 1 - i];
            buf[len - 1 - i] = t;
        }
        return new string(buf, 0, len);
    }

    private static string SanitizeSheetName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "Sheet1";
        var sb = new StringBuilder(Mathf.Min(31, name.Length));
        for (int i = 0; i < name.Length && sb.Length < 31; i++)
        {
            char c = name[i];
            if (c == '\\' || c == '/' || c == '?' || c == '*' || c == '[' || c == ']' || c == ':')
                continue;
            sb.Append(c);
        }
        return sb.Length > 0 ? sb.ToString() : "Sheet1";
    }

    private static string XmlEscapeText(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }

    private static string XmlEscapeAttr(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return XmlEscapeText(s).Replace("\"", "&quot;");
    }

    /// <summary>UTF-8 BOM ile diske düz yazar (şifresiz yol — test/debug).</summary>
    public static void WriteCsvFilePlain(string path, string[] headers, List<string[]> rows)
    {
        if (string.IsNullOrEmpty(path)) return;
        File.WriteAllText(path, BuildCsv(headers, rows), Utf8Bom);
    }
}

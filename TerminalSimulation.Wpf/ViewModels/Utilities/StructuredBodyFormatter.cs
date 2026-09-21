using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace TerminalSimulation.Wpf.ViewModels.Utilities;

internal static class StructuredBodyFormatter
{
    private static readonly JsonSerializerOptions IndentedJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly JsonSerializerOptions CompactJsonOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    internal static bool IsJson(string? contentType) =>
        contentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true;

    internal static bool IsXml(string? contentType) =>
        contentType?.Contains("xml", StringComparison.OrdinalIgnoreCase) == true;

    internal static string GetLanguageName(string? contentType) =>
        IsJson(contentType) ? "JSON" : IsXml(contentType) ? "XML" : "纯文本";

    internal static bool TryTransform(
        string? source,
        string? contentType,
        bool indented,
        out string result,
        out string error)
    {
        result = source ?? string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(result))
        {
            return true;
        }

        try
        {
            if (IsJson(contentType))
            {
                using var document = JsonDocument.Parse(result);
                result = JsonSerializer.Serialize(
                    document.RootElement,
                    indented ? IndentedJsonOptions : CompactJsonOptions);
                return true;
            }

            if (IsXml(contentType))
            {
                var document = XDocument.Parse(result, LoadOptions.SetLineInfo);
                result = indented
                    ? FormatXml(document)
                    : document.ToString(SaveOptions.DisableFormatting);
                return true;
            }

            error = "当前 Content-Type 不是 JSON 或 XML。";
            return false;
        }
        catch (JsonException exception)
        {
            var line = (exception.LineNumber ?? 0) + 1;
            var column = (exception.BytePositionInLine ?? 0) + 1;
            error = $"JSON 第 {line} 行、第 {column} 列: {exception.Message}";
            return false;
        }
        catch (XmlException exception)
        {
            error = $"XML 第 {exception.LineNumber} 行、第 {exception.LinePosition} 列: {exception.Message}";
            return false;
        }
    }

    internal static bool TryValidate(string? source, string? contentType, out string error)
    {
        if (!IsJson(contentType) && !IsXml(contentType))
        {
            error = string.Empty;
            return true;
        }

        return TryTransform(source, contentType, indented: false, out _, out error);
    }

    private static string FormatXml(XDocument document)
    {
        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            NewLineChars = Environment.NewLine,
            NewLineHandling = NewLineHandling.Replace,
            OmitXmlDeclaration = document.Declaration is null
        };

        using var writer = new StringWriter();
        using (var xmlWriter = XmlWriter.Create(writer, settings))
        {
            document.Save(xmlWriter);
        }
        return writer.ToString().TrimEnd();
    }
}

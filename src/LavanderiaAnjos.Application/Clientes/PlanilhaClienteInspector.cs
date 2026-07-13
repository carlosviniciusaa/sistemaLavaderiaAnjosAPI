using System.IO.Compression;
using System.Xml.Linq;

namespace LavanderiaAnjos.Application.Clientes;

public sealed class PlanilhaClienteInspector : IPlanilhaClienteInspector
{
    private static readonly XNamespace SpreadsheetNamespace =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public IReadOnlyList<string> ObterNomesDasAbas(Stream planilha)
    {
        ArgumentNullException.ThrowIfNull(planilha);

        using var archive = new ZipArchive(planilha, ZipArchiveMode.Read, leaveOpen: true);
        var workbookEntry = archive.GetEntry("xl/workbook.xml")
            ?? throw new InvalidDataException("A planilha não contém a estrutura xl/workbook.xml esperada.");

        using var workbookStream = workbookEntry.Open();
        var document = XDocument.Load(workbookStream, LoadOptions.None);

        return document
            .Descendants(SpreadsheetNamespace + "sheet")
            .Select(sheet => (string?)sheet.Attribute("name"))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .ToArray();
    }
}

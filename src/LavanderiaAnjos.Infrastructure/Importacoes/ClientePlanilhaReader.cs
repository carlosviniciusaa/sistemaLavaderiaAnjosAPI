using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using ClosedXML.Graphics;
using LavanderiaAnjos.Application.Importacoes;

namespace LavanderiaAnjos.Infrastructure.Importacoes;

public sealed class ClientePlanilhaReader : IClientePlanilhaReader
{
    private const int MaxRows = 10_000;
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private static readonly SheetDefinition[] Definitions =
    [
        new("Uniformes e hoteis",
        [
            "Código", "Nome", "CNPJ ou CPF", "Produtor Rural", "I.E.", "I.M.", "Início",
            "Logradouro", "Nº", "Bairro", "Cidade", "UF", "CEP", "Telefone", "E-mail Envio", "E-mail NF"
        ], true),
        new("Consumidor final",
        [
            "Nome", "CNPJ ou CPF", "I.E.", "I.M.", "CEP", "Logradouro", "Nº", "Complemento",
            "Bairro", "Cidade", "UF", "Telefone", "E-mail Envio", "E-mail NF"
        ], false)
    ];

    public ImportacaoLeituraResultado Ler(Stream planilha)
    {
        ArgumentNullException.ThrowIfNull(planilha);

        try
        {
            var fontPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts", "arial.ttf");
            using var fontStream = File.OpenRead(fontPath);
            var loadOptions = new LoadOptions
            {
                GraphicEngine = DefaultGraphicEngine.CreateOnlyWithFonts(fontStream, [])
            };
            using var workbook = new XLWorkbook(planilha, loadOptions);
            var metrics = new List<PlanilhaAbaMetricas>();
            var items = new List<ImportacaoItemEstagio>();

            foreach (var definition in Definitions)
            {
                var sheet = workbook.Worksheets.FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, definition.Name, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidDataException($"A aba obrigatória '{definition.Name}' não foi encontrada.");
                ReadSheet(sheet, definition, metrics, items);
            }

            var combined = new PlanilhaMetricas(
                metrics,
                metrics.Sum(item => item.UltimaLinhaFisica),
                metrics.Sum(item => item.LinhasCabecalho),
                metrics.Sum(item => item.LinhasTotalmenteVazias),
                metrics.Sum(item => item.LinhasParciaisDescartadas),
                metrics.Sum(item => item.LinhasOcultas),
                metrics.Sum(item => item.LinhasFiltradas),
                metrics.Sum(item => item.RegistrosConsiderados));
            return new(combined, items);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidDataException("O arquivo não é uma planilha .xlsx válida ou está corrompido.", exception);
        }
    }

    private static void ReadSheet(
        IXLWorksheet sheet,
        SheetDefinition definition,
        List<PlanilhaAbaMetricas> metrics,
        List<ImportacaoItemEstagio> items)
    {
        var lastRow = sheet.LastRowUsed(XLCellsUsedOptions.All)?.RowNumber() ?? 0;
        if (lastRow > MaxRows)
        {
            throw new InvalidDataException($"A aba '{sheet.Name}' excede o limite seguro de {MaxRows:N0} linhas.");
        }

        if (lastRow == 0)
        {
            throw new InvalidDataException($"A aba '{sheet.Name}' está vazia.");
        }

        var headers = sheet.Row(1).CellsUsed(XLCellsUsedOptions.All)
            .Select(cell => new { Name = Clean(cell.GetFormattedString(PtBr)), Column = cell.Address.ColumnNumber })
            .Where(header => header.Name is not null)
            .GroupBy(header => header.Name!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Column, StringComparer.OrdinalIgnoreCase);

        var missing = definition.RequiredHeaders.Where(header => !headers.ContainsKey(header)).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidDataException($"A aba '{sheet.Name}' não contém os cabeçalhos obrigatórios: {string.Join(", ", missing)}.");
        }

        var empty = 0;
        var partial = 0;
        var hidden = 0;
        var filtered = 0;
        var considered = 0;
        var autoFilterRange = sheet.AutoFilter.IsEnabled ? sheet.AutoFilter.Range : null;

        for (var rowNumber = 2; rowNumber <= lastRow; rowNumber++)
        {
            var row = sheet.Row(rowNumber);
            if (row.IsHidden)
            {
                hidden++;
                if (autoFilterRange is not null && rowNumber >= autoFilterRange.RangeAddress.FirstAddress.RowNumber &&
                    rowNumber <= autoFilterRange.RangeAddress.LastAddress.RowNumber)
                {
                    filtered++;
                }
            }

            var values = headers.ToDictionary(pair => pair.Key, pair => Text(row.Cell(pair.Value)), StringComparer.OrdinalIgnoreCase);
            if (values.Values.All(string.IsNullOrWhiteSpace))
            {
                empty++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(values["Nome"]) && string.IsNullOrWhiteSpace(values["CNPJ ou CPF"]))
            {
                partial++;
                continue;
            }

            considered++;
            items.Add(CreateItem(row, values, definition.HasBusinessFields));
        }

        metrics.Add(new(sheet.Name, lastRow, 1, empty, partial, hidden, filtered, considered));
    }

    private static ImportacaoItemEstagio CreateItem(IXLRow row, Dictionary<string, string?> values, bool business)
    {
        var errors = new List<string>();
        var code = business ? ParseCode(values["Código"], errors) : null;
        var rural = business && ParseRural(values["Produtor Rural"], errors);
        var startDate = business ? ParseDate(row.Worksheet.Cell(row.RowNumber(), FindColumn(row.Worksheet, "Início")), values["Início"], errors) : null;

        var document = values["CNPJ ou CPF"];
        return new ImportacaoItemEstagio
        {
            Aba = row.Worksheet.Name,
            Linha = row.RowNumber(),
            DocumentoEncontrado = document,
            ErrosLeitura = errors,
            Dados = new(
                code,
                values["Nome"],
                document,
                rural,
                values["I.E."],
                values["I.M."],
                startDate,
                values["CEP"],
                values["Logradouro"],
                values["Nº"],
                business ? null : values["Complemento"],
                values["Bairro"],
                values["Cidade"],
                values["UF"],
                values["Telefone"],
                values["E-mail Envio"],
                values["E-mail NF"])
        };
    }

    private static int FindColumn(IXLWorksheet sheet, string header) =>
        sheet.Row(1).CellsUsed(XLCellsUsedOptions.All)
            .First(cell => string.Equals(Clean(cell.GetFormattedString(PtBr)), header, StringComparison.OrdinalIgnoreCase))
            .Address.ColumnNumber;

    private static int? ParseCode(string? value, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.EndsWith(",0", StringComparison.Ordinal) ? value[..^2] : value;
        if (int.TryParse(normalized, NumberStyles.Integer, PtBr, out var result) && result >= 0)
        {
            return result;
        }

        errors.Add("O código legado não pôde ser convertido para um número inteiro.");
        return null;
    }

    private static bool ParseRural(string? value, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Trim().ToUpperInvariant() switch
        {
            "SIM" or "S" or "1" or "TRUE" or "X" => true,
            "NÃO" or "NAO" or "N" or "0" or "FALSE" => false,
            _ => UnknownRural(errors)
        };
    }

    private static bool UnknownRural(List<string> errors)
    {
        errors.Add("O valor de produtor rural é desconhecido.");
        return false;
    }

    private static DateOnly? ParseDate(IXLCell cell, string? value, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (cell.TryGetValue<DateTime>(out var typedDate))
        {
            return DateOnly.FromDateTime(typedDate);
        }

        if (double.TryParse(value, NumberStyles.Float, PtBr, out var serial) && serial is > 0 and < 2_958_466)
        {
            try
            {
                return DateOnly.FromDateTime(DateTime.FromOADate(serial));
            }
            catch (ArgumentException)
            {
                // The textual parsing below will report the validation error.
            }
        }

        if (DateTime.TryParse(value, PtBr, DateTimeStyles.AllowWhiteSpaces, out var parsed) ||
            DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out parsed))
        {
            return DateOnly.FromDateTime(parsed);
        }

        errors.Add("A data de início não pôde ser interpretada.");
        return null;
    }

    private static string? Text(IXLCell cell) => Clean(cell.GetFormattedString(PtBr));

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var builder = new StringBuilder(value.Length);
        var previousSpace = false;
        foreach (var character in value.Normalize())
        {
            if (character is '\u200B' or '\u200C' or '\u200D' or '\uFEFF' or '\u00AD')
            {
                continue;
            }

            if (char.IsWhiteSpace(character) || char.IsControl(character))
            {
                if (!previousSpace)
                {
                    builder.Append(' ');
                    previousSpace = true;
                }
                continue;
            }

            builder.Append(character);
            previousSpace = false;
        }

        var result = builder.ToString().Trim();
        return result.Length == 0 ? null : result;
    }

    private sealed record SheetDefinition(string Name, string[] RequiredHeaders, bool HasBusinessFields);
}

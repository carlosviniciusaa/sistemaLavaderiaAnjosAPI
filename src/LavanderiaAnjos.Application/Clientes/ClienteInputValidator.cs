using System.Net.Mail;
using LavanderiaAnjos.Domain.Clientes;

namespace LavanderiaAnjos.Application.Clientes;

public static class ClienteInputValidator
{
    public static ServiceResult<NormalizedClienteData> Validate(IClienteRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var nome = TrimToNull(request.NomeRazaoSocial);
        var documento = DocumentoFiscal.Normalize(request.Documento);

        if (nome is null)
        {
            errors["nomeRazaoSocial"] = ["Informe o nome ou razão social."];
        }
        else if (nome.Length > 200)
        {
            errors["nomeRazaoSocial"] = ["O nome ou razão social deve ter no máximo 200 caracteres."];
        }

        if (documento.Length == 0)
        {
            errors["documento"] = ["Informe o CPF ou CNPJ."];
        }
        else if (!DocumentoFiscal.TryValidate(documento, out _))
        {
            errors["documento"] = ["Informe um CPF ou CNPJ válido."];
        }

        var cep = DigitsOnlyOrNull(request.Cep);
        if (cep?.Length > 8)
        {
            errors["cep"] = ["O CEP deve ter no máximo oito dígitos."];
        }

        var telefone = NormalizePhone(request.Telefone);
        if (telefone?.Length > 20)
        {
            errors["telefone"] = ["O telefone deve ter no máximo 20 caracteres."];
        }

        var uf = TrimToNull(request.Uf)?.ToUpperInvariant();
        if (uf?.Length > 2)
        {
            errors["uf"] = ["A UF deve ter no máximo dois caracteres."];
        }

        var emailEnvio = NormalizeEmails(request.EmailEnvio, "emailEnvio", errors);
        var emailNotaFiscal = NormalizeEmails(request.EmailNotaFiscal, "emailNotaFiscal", errors);

        ValidateMaxLength(request.InscricaoEstadual, 30, "inscricaoEstadual", "Inscrição estadual", errors);
        ValidateMaxLength(request.InscricaoMunicipal, 30, "inscricaoMunicipal", "Inscrição municipal", errors);
        ValidateMaxLength(request.Logradouro, 200, "logradouro", "Logradouro", errors);
        ValidateMaxLength(request.Numero, 30, "numero", "Número", errors);
        ValidateMaxLength(request.Complemento, 150, "complemento", "Complemento", errors);
        ValidateMaxLength(request.Bairro, 100, "bairro", "Bairro", errors);
        ValidateMaxLength(request.Cidade, 100, "cidade", "Cidade", errors);
        ValidateMaxLength(request.Observacoes, 2000, "observacoes", "Observações", errors);

        if (errors.Count > 0)
        {
            return ServiceResult.Failure<NormalizedClienteData>(ServiceErrorKind.Validation, errors);
        }

        DocumentoFiscal.TryValidate(documento, out var tipoPessoa);
        var normalized = new NormalizedClienteData(
            request.CodigoLegado,
            nome!,
            documento,
            tipoPessoa,
            request.ProdutorRural,
            TrimToNull(request.InscricaoEstadual),
            TrimToNull(request.InscricaoMunicipal),
            request.DataInicioRelacionamento,
            cep,
            TrimToNull(request.Logradouro),
            TrimToNull(request.Numero),
            TrimToNull(request.Complemento),
            TrimToNull(request.Bairro),
            TrimToNull(request.Cidade),
            uf,
            telefone,
            emailEnvio,
            emailNotaFiscal,
            TrimToNull(request.Observacoes));

        return ServiceResult.Success(normalized);
    }

    private static void ValidateMaxLength(
        string? value,
        int maxLength,
        string key,
        string label,
        Dictionary<string, string[]> errors)
    {
        if (TrimToNull(value)?.Length > maxLength)
        {
            errors[key] = [$"{label} deve ter no máximo {maxLength} caracteres."];
        }
    }

    private static string? NormalizeEmails(
        string? value,
        string key,
        Dictionary<string, string[]> errors)
    {
        var trimmed = TrimToNull(value);
        if (trimmed is null)
        {
            return null;
        }

        var emails = trimmed.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (emails.Any(email => !MailAddress.TryCreate(email, out var parsed) || parsed.Address != email))
        {
            errors[key] = ["Informe endereços de e-mail válidos, separados por ponto e vírgula."];
            return trimmed;
        }

        var normalized = string.Join(';', emails);
        if (normalized.Length > 500)
        {
            errors[key] = ["A lista de e-mails deve ter no máximo 500 caracteres."];
        }

        return normalized;
    }

    private static string? DigitsOnlyOrNull(string? value)
    {
        var digits = string.Concat((value ?? string.Empty).Where(char.IsAsciiDigit));
        return digits.Length == 0 ? null : digits;
    }

    private static string? NormalizePhone(string? value)
    {
        var trimmed = TrimToNull(value);
        if (trimmed is null)
        {
            return null;
        }

        return string.Concat(trimmed.Where(character => character is not (' ' or '(' or ')' or '-')));
    }

    private static string? TrimToNull(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}

public sealed record NormalizedClienteData(
    int? CodigoLegado,
    string NomeRazaoSocial,
    string Documento,
    TipoPessoa TipoPessoa,
    bool ProdutorRural,
    string? InscricaoEstadual,
    string? InscricaoMunicipal,
    DateOnly? DataInicioRelacionamento,
    string? Cep,
    string? Logradouro,
    string? Numero,
    string? Complemento,
    string? Bairro,
    string? Cidade,
    string? Uf,
    string? Telefone,
    string? EmailEnvio,
    string? EmailNotaFiscal,
    string? Observacoes);

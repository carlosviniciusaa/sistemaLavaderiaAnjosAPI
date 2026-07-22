using System.Text.RegularExpressions;

namespace LavanderiaAnjos.Application.Administradores;

public static partial class AdministradorInputValidator
{
    public const int NomeMaxLength = 150;
    public const int LoginMaxLength = 100;
    public const int LoginMinLength = 3;
    public const int SenhaMinLength = 8;
    public const int SenhaMaxLength = 256;

    public static AdministradorServiceResult<(string Nome, string Login, string LoginNormalizado)> ValidateIdentity(
        string? nome,
        string? login)
    {
        var errors = new Dictionary<string, string[]>();
        var normalizedName = nome?.Trim() ?? string.Empty;
        var normalizedLogin = NormalizeLogin(login);

        if (normalizedName.Length == 0)
            errors["nome"] = ["Informe o nome do administrador."];
        else if (normalizedName.Length > NomeMaxLength)
            errors["nome"] = [$"O nome deve ter no máximo {NomeMaxLength} caracteres."];

        if (normalizedLogin.Length == 0)
            errors["login"] = ["Informe o login."];
        else if (normalizedLogin.Length < LoginMinLength)
            errors["login"] = [$"O login deve ter pelo menos {LoginMinLength} caracteres."];
        else if (normalizedLogin.Length > LoginMaxLength)
            errors["login"] = [$"O login deve ter no máximo {LoginMaxLength} caracteres."];
        else if (!LoginPattern().IsMatch(normalizedLogin))
            errors["login"] = ["Use apenas letras, números, ponto, hífen e sublinhado, sem espaços."];

        return errors.Count == 0
            ? AdministradorServiceResult.Success((normalizedName, normalizedLogin, normalizedLogin))
            : AdministradorServiceResult.Failure<(string, string, string)>(AdministradorServiceErrorKind.Validation, errors);
    }

    public static AdministradorServiceResult<string> ValidatePassword(string? password, string? confirmation)
    {
        var errors = new Dictionary<string, string[]>();
        var value = password ?? string.Empty;

        if (value.Length == 0)
            errors["novaSenha"] = ["Informe a senha."];
        else if (value.Length < SenhaMinLength)
            errors["novaSenha"] = [$"A senha deve ter pelo menos {SenhaMinLength} caracteres."];
        else if (value.Length > SenhaMaxLength)
            errors["novaSenha"] = [$"A senha deve ter no máximo {SenhaMaxLength} caracteres."];
        else if (!value.Any(char.IsLetter))
            errors["novaSenha"] = ["A senha deve conter pelo menos uma letra."];
        else if (!value.Any(char.IsDigit))
            errors["novaSenha"] = ["A senha deve conter pelo menos um número."];

        if (!string.Equals(value, confirmation, StringComparison.Ordinal))
            errors["confirmacaoSenha"] = ["A confirmação da senha não confere."];

        return errors.Count == 0
            ? AdministradorServiceResult.Success(value)
            : AdministradorServiceResult.Failure<string>(AdministradorServiceErrorKind.Validation, errors);
    }

    public static string NormalizeLogin(string? login) => (login ?? string.Empty).Trim().ToLowerInvariant();

    [GeneratedRegex("^[a-z0-9._-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex LoginPattern();
}

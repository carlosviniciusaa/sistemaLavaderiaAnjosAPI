using LavanderiaAnjos.Domain.Clientes;

namespace LavanderiaAnjos.Application.Clientes;

public static class DocumentoFiscal
{
    public static string Normalize(string? value) =>
        string.Concat((value ?? string.Empty).Where(char.IsAsciiDigit));

    public static bool TryValidate(string? value, out TipoPessoa tipoPessoa)
    {
        var digits = Normalize(value);
        tipoPessoa = digits.Length == 14 ? TipoPessoa.PessoaJuridica : TipoPessoa.PessoaFisica;

        if (digits.Length is not (11 or 14) || digits.All(digit => digit == digits[0]))
        {
            return false;
        }

        return digits.Length == 11 ? IsValidCpf(digits) : IsValidCnpj(digits);
    }

    public static string Format(string? value)
    {
        var digits = Normalize(value);
        return digits.Length switch
        {
            11 => $"{digits[..3]}.{digits[3..6]}.{digits[6..9]}-{digits[9..]}",
            14 => $"{digits[..2]}.{digits[2..5]}.{digits[5..8]}/{digits[8..12]}-{digits[12..]}",
            _ => digits
        };
    }

    private static bool IsValidCpf(string digits)
    {
        var first = CalculateDigit(digits.AsSpan(0, 9), [10, 9, 8, 7, 6, 5, 4, 3, 2]);
        var second = CalculateDigit(digits.AsSpan(0, 10), [11, 10, 9, 8, 7, 6, 5, 4, 3, 2]);
        return first == digits[9] - '0' && second == digits[10] - '0';
    }

    private static bool IsValidCnpj(string digits)
    {
        var first = CalculateDigit(digits.AsSpan(0, 12), [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
        var second = CalculateDigit(digits.AsSpan(0, 13), [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
        return first == digits[12] - '0' && second == digits[13] - '0';
    }

    private static int CalculateDigit(ReadOnlySpan<char> digits, ReadOnlySpan<int> weights)
    {
        var sum = 0;
        for (var index = 0; index < digits.Length; index++)
        {
            sum += (digits[index] - '0') * weights[index];
        }

        var remainder = sum % 11;
        return remainder < 2 ? 0 : 11 - remainder;
    }
}

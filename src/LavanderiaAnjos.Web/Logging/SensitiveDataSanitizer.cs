using System.Text.RegularExpressions;

namespace LavanderiaAnjos.Web.Logging;

internal static partial class SensitiveDataSanitizer
{
    public static string Redact(string value)
    {
        var sanitized = CredentialRegex().Replace(value, "$1=<omitido>");
        sanitized = EmailRegex().Replace(sanitized, "<e-mail omitido>");
        return DocumentRegex().Replace(sanitized, "<documento omitido>");
    }

    [GeneratedRegex(@"(?i)\b(password|pwd|user\s*id|uid)\s*=\s*[^;\s]*", RegexOptions.CultureInvariant)]
    private static partial Regex CredentialRegex();

    [GeneratedRegex(@"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"(?<!\d)(?:\d[.\-/\s]?){11,14}(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex DocumentRegex();
}

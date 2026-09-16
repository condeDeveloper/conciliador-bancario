using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Conciliador.Core.Conciliacao;

/// <summary>Normalização e similaridade de descrições de lançamentos.</summary>
public static partial class Texto
{
    private static readonly HashSet<string> Ruido = new(StringComparer.Ordinal)
    {
        "de", "da", "do", "das", "dos", "e", "a", "o", "em", "para", "por", "com", "ltda", "sa", "me", "eireli", "pagamento", "pagto", "pgto", "transferencia", "transf", "ted", "doc", "pix", "ref", "tarifa", "boleto", "titulo", "compra", "cartao",
    };

    /// <summary>Minúsculas, sem acento, sem pontuação, sem palavras vazias de sentido e sem números soltos (datas, códigos).</summary>
    public static IReadOnlySet<string> Tokens(string texto)
    {
        var limpo = Normalizar(texto);
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in limpo.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (t.Length >= 2 && !Ruido.Contains(t) && !t.All(char.IsDigit)) tokens.Add(t);
        return tokens;
    }

    public static string Normalizar(string texto)
    {
        var sb = new StringBuilder(texto.Length);
        foreach (var c in texto.Normalize(NormalizationForm.FormD))
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }
        return Espacos().Replace(sb.ToString(), " ").Trim();
    }

    /// <summary>Índice de Jaccard entre os conjuntos de tokens: 0 sem nada em comum, 1 idênticos.</summary>
    public static double Similaridade(string a, string b)
    {
        var ta = Tokens(a);
        var tb = Tokens(b);
        if (ta.Count == 0 || tb.Count == 0) return 0;
        var inter = ta.Intersect(tb).Count();
        var uniao = ta.Count + tb.Count - inter;
        return (double)inter / uniao;
    }

    /// <summary>Extrai números longos (6 ou mais dígitos) que costumam ser número de documento, nota ou boleto.</summary>
    public static IReadOnlySet<string> Numeros(string texto) =>
        NumerosLongos().Matches(texto).Select(m => m.Value.TrimStart('0')).Where(n => n.Length > 0).ToHashSet(StringComparer.Ordinal);

    [GeneratedRegex(@"\s+")]
    private static partial Regex Espacos();

    [GeneratedRegex(@"\d{6,}")]
    private static partial Regex NumerosLongos();
}

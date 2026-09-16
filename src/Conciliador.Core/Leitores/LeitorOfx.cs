using System.Globalization;
using System.Text.RegularExpressions;
using Conciliador.Core.Modelo;

namespace Conciliador.Core.Leitores;

/// <summary>
/// Lê OFX 1.x (SGML, tags sem fechamento) e 2.x (XML). Em vez de um parser completo, extrai por regex os blocos
/// STMTTRN e os campos de cabeçalho e saldo, que é o que interessa para conciliar. Tolera CRLF, tabs, tags em
/// minúsculas e o cabeçalho de texto do OFX 1.
/// </summary>
public static partial class LeitorOfx
{
    public static Extrato Ler(string conteudo)
    {
        if (string.IsNullOrWhiteSpace(conteudo) || !conteudo.Contains("<OFX", StringComparison.OrdinalIgnoreCase))
            throw new ArquivoInvalidoException("conteúdo não parece um arquivo OFX");

        var banco = Campo(conteudo, "BANKID") ?? "";
        var agencia = Campo(conteudo, "BRANCHID") ?? "";
        var conta = Campo(conteudo, "ACCTID") ?? "";
        var inicio = Data(Campo(conteudo, "DTSTART"));
        var fim = Data(Campo(conteudo, "DTEND"));

        decimal? saldo = null;
        var ledger = LedgerBal().Match(conteudo);
        if (ledger.Success) saldo = Numero(Campo(ledger.Value, "BALAMT"));

        var lancamentos = new List<Lancamento>();
        var vistos = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in StmtTrn().Matches(conteudo))
        {
            var bloco = m.Groups[1].Value;
            var valor = Numero(Campo(bloco, "TRNAMT")) ?? throw new ArquivoInvalidoException("STMTTRN sem TRNAMT");
            var data = Data(Campo(bloco, "DTPOSTED")) ?? throw new ArquivoInvalidoException("STMTTRN sem DTPOSTED");
            var fitid = Campo(bloco, "FITID") ?? $"{data:yyyyMMdd}-{valor}-{lancamentos.Count}";
            if (!vistos.Add(fitid)) continue; // bancos repetem FITID quando o período se sobrepõe
            var memo = Campo(bloco, "MEMO");
            var nome = Campo(bloco, "NAME");
            var descricao = string.Join(" ", new[] { nome, memo }.Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
            var documento = Campo(bloco, "CHECKNUM") ?? Campo(bloco, "REFNUM");
            lancamentos.Add(new Lancamento(fitid, Origem.Banco, data, valor, descricao, string.IsNullOrWhiteSpace(documento) ? null : documento.Trim()));
        }

        return new Extrato(banco, agencia, conta, inicio, fim, saldo, lancamentos);
    }

    /// <summary>Valor de uma tag: tudo após &lt;TAG&gt; até a próxima tag ou fim de linha (SGML) ou até &lt;/TAG&gt; (XML).</summary>
    internal static string? Campo(string texto, string tag)
    {
        var m = Regex.Match(texto, $@"<{tag}>\s*([^<\r\n]*)", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        var v = m.Groups[1].Value.Trim();
        return v.Length == 0 ? null : v;
    }

    /// <summary>Datas OFX: YYYYMMDD ou YYYYMMDDHHMMSS[.XXX][-3:BRT]. Só o dia interessa.</summary>
    internal static DateOnly? Data(string? s)
    {
        if (s is null || s.Length < 8) return null;
        return DateOnly.TryParseExact(s[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    /// <summary>Números OFX usam ponto decimal, mas alguns bancos brasileiros mandam vírgula.</summary>
    internal static decimal? Numero(string? s)
    {
        if (s is null) return null;
        s = s.Replace(",", ".");
        return decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    [GeneratedRegex(@"<STMTTRN>(.*?)(?=<STMTTRN>|</STMTTRN>|</BANKTRANLIST>)", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex StmtTrn();

    [GeneratedRegex(@"<LEDGERBAL>.*?(?=</LEDGERBAL>|<AVAILBAL>|</STMTRS>)", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex LedgerBal();
}

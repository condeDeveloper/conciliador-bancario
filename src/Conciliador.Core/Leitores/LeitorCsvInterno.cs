using System.Globalization;
using Conciliador.Core.Modelo;

namespace Conciliador.Core.Leitores;

/// <summary>
/// Lançamentos do sistema interno (ERP, contas a pagar e receber) em CSV com cabeçalho:
/// <c>id;data;valor;descricao;documento</c>. Separador ; ou , detectado pela primeira linha; datas em dd/MM/yyyy ou
/// yyyy-MM-dd; valores com vírgula ou ponto decimal; aspas opcionais.
/// </summary>
public static class LeitorCsvInterno
{
    public static IReadOnlyList<Lancamento> Ler(string conteudo)
    {
        var linhas = conteudo.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Trim().Length > 0).ToList();
        if (linhas.Count < 2) throw new ArquivoInvalidoException("CSV precisa de cabeçalho e ao menos uma linha");
        var separador = linhas[0].Count(c => c == ';') >= linhas[0].Count(c => c == ',') ? ';' : ',';
        var cabecalho = Dividir(linhas[0], separador).Select(c => c.Trim().ToLowerInvariant()).ToList();
        int Col(string nome) => cabecalho.IndexOf(nome) is var i and >= 0 ? i : throw new ArquivoInvalidoException($"coluna '{nome}' ausente no cabeçalho");
        int iId = Col("id"), iData = Col("data"), iValor = Col("valor"), iDesc = Col("descricao");
        var iDoc = cabecalho.IndexOf("documento");

        var saida = new List<Lancamento>();
        foreach (var (linha, n) in linhas.Skip(1).Select((l, n) => (l, n + 2)))
        {
            var campos = Dividir(linha, separador);
            if (campos.Count <= Math.Max(iValor, Math.Max(iData, iDesc))) throw new ArquivoInvalidoException($"linha {n}: colunas insuficientes");
            var data = LerData(campos[iData].Trim()) ?? throw new ArquivoInvalidoException($"linha {n}: data inválida '{campos[iData]}'");
            var valor = LerValor(campos[iValor].Trim()) ?? throw new ArquivoInvalidoException($"linha {n}: valor inválido '{campos[iValor]}'");
            var doc = iDoc >= 0 && iDoc < campos.Count ? campos[iDoc].Trim() : "";
            saida.Add(new Lancamento(campos[iId].Trim(), Origem.Interno, data, valor, campos[iDesc].Trim(), doc.Length == 0 ? null : doc));
        }
        return saida;
    }

    internal static DateOnly? LerData(string s)
    {
        foreach (var fmt in new[] { "dd/MM/yyyy", "yyyy-MM-dd", "ddMMyyyy", "yyyyMMdd" })
            if (DateOnly.TryParseExact(s, fmt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d;
        return null;
    }

    internal static decimal? LerValor(string s)
    {
        s = s.Replace("R$", "").Replace(" ", "");
        if (s.Contains(',') && s.Contains('.')) s = s.Replace(".", "").Replace(',', '.'); // 1.234,56
        else if (s.Contains(',')) s = s.Replace(',', '.');
        return decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    /// <summary>Divide respeitando aspas duplas ("" escapa aspas).</summary>
    internal static List<string> Dividir(string linha, char separador)
    {
        var campos = new List<string>();
        var atual = new System.Text.StringBuilder();
        var entreAspas = false;
        for (var i = 0; i < linha.Length; i++)
        {
            var c = linha[i];
            if (entreAspas)
            {
                if (c == '"' && i + 1 < linha.Length && linha[i + 1] == '"') { atual.Append('"'); i++; }
                else if (c == '"') entreAspas = false;
                else atual.Append(c);
            }
            else if (c == '"' && atual.Length == 0) entreAspas = true; // aspas só abrem no início do campo; no meio são literais
            else if (c == separador) { campos.Add(atual.ToString()); atual.Clear(); }
            else atual.Append(c);
        }
        campos.Add(atual.ToString());
        return campos;
    }
}

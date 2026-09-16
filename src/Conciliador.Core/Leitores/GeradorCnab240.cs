using System.Globalization;
using System.Text;
using Conciliador.Core.Modelo;

namespace Conciliador.Core.Leitores;

/// <summary>Gera um arquivo de extrato CNAB 240 a partir de lançamentos, o inverso do leitor. Usado nos testes e nos exemplos.</summary>
public static class GeradorCnab240
{
    public static string Gerar(string banco, string agencia, string conta, string dvConta, DateOnly inicio, DateOnly fim, decimal saldoFinal, IReadOnlyList<Lancamento> lancamentos, string empresa = "EMPRESA EXEMPLO LTDA")
    {
        var linhas = new List<string>();
        linhas.Add(Linha(l =>
        {
            Set(l, 1, 3, banco); Set(l, 4, 7, "0000"); Set(l, 8, 8, "0");
            Set(l, 18, 18, "2"); Set(l, 19, 32, "12345678000199"); Set(l, 73, 102, empresa); Set(l, 103, 132, "BANCO EXEMPLO");
            Set(l, 143, 143, "2"); Set(l, 144, 151, fim.ToString("ddMMyyyy", CultureInfo.InvariantCulture)); Set(l, 152, 157, "120000"); Set(l, 158, 163, "000001"); Set(l, 164, 166, "089");
        }));
        linhas.Add(Linha(l =>
        {
            Set(l, 1, 3, banco); Set(l, 4, 7, "0001"); Set(l, 8, 8, "1"); Set(l, 9, 9, "E"); Set(l, 10, 11, "04"); Set(l, 14, 16, "040");
            Set(l, 18, 18, "2"); Set(l, 19, 32, "12345678000199"); Set(l, 53, 57, agencia.PadLeft(5, '0')); Set(l, 59, 70, conta.PadLeft(12, '0')); Set(l, 71, 71, dvConta);
            Set(l, 73, 102, empresa); Set(l, 143, 150, inicio.ToString("ddMMyyyy", CultureInfo.InvariantCulture));
        }));
        var seq = 0;
        foreach (var lc in lancamentos)
        {
            seq++;
            var n = seq;
            linhas.Add(Linha(l =>
            {
                Set(l, 1, 3, banco); Set(l, 4, 7, "0001"); Set(l, 8, 8, "3"); Set(l, 9, 13, n.ToString("D5", CultureInfo.InvariantCulture)); Set(l, 14, 14, "E");
                Set(l, 53, 57, agencia.PadLeft(5, '0')); Set(l, 59, 70, conta.PadLeft(12, '0')); Set(l, 71, 71, dvConta);
                Set(l, 143, 150, lc.Data.ToString("ddMMyyyy", CultureInfo.InvariantCulture));
                Set(l, 151, 168, Centavos(Math.Abs(lc.Valor), 18)); Set(l, 169, 169, lc.Valor < 0 ? "D" : "C");
                Set(l, 170, 172, "101"); Set(l, 173, 176, "0001"); Set(l, 177, 201, lc.Descricao.Length > 25 ? lc.Descricao[..25] : lc.Descricao);
                Set(l, 202, 240, (lc.Documento ?? "").PadLeft(39, '0'));
            }));
        }
        var registrosLote = seq + 2;
        linhas.Add(Linha(l =>
        {
            Set(l, 1, 3, banco); Set(l, 4, 7, "0001"); Set(l, 8, 8, "5"); Set(l, 18, 23, registrosLote.ToString("D6", CultureInfo.InvariantCulture));
            Set(l, 60, 77, Centavos(Math.Abs(saldoFinal), 18)); Set(l, 78, 78, saldoFinal < 0 ? "D" : "C"); Set(l, 79, 86, fim.ToString("ddMMyyyy", CultureInfo.InvariantCulture));
        }));
        var total = linhas.Count + 1;
        linhas.Add(Linha(l =>
        {
            Set(l, 1, 3, banco); Set(l, 4, 7, "9999"); Set(l, 8, 8, "9"); Set(l, 18, 23, "000001"); Set(l, 24, 29, total.ToString("D6", CultureInfo.InvariantCulture));
        }));
        return string.Join("\r\n", linhas) + "\r\n";
    }

    private static string Linha(Action<char[]> preencher)
    {
        var l = new string(' ', 240).ToCharArray();
        preencher(l);
        return new string(l);
    }

    private static void Set(char[] linha, int de, int ate, string valor)
    {
        var tamanho = ate - de + 1;
        var texto = RemoverAcentos(valor).ToUpperInvariant();
        texto = texto.Length > tamanho ? texto[..tamanho] : texto.PadRight(tamanho);
        texto.CopyTo(0, linha, de - 1, tamanho);
    }

    private static string Centavos(decimal valor, int tamanho) => ((long)Math.Round(valor * 100)).ToString(CultureInfo.InvariantCulture).PadLeft(tamanho, '0');

    private static string RemoverAcentos(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s.Normalize(NormalizationForm.FormD))
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString();
    }
}

using System.Globalization;
using Conciliador.Core.Modelo;

namespace Conciliador.Core.Leitores;

/// <summary>
/// Lê o arquivo de extrato para conciliação bancária no padrão FEBRABAN CNAB 240: header de arquivo (tipo 0),
/// header de lote (tipo 1), segmento E (tipo 3, um por lançamento), trailer de lote (tipo 5) e trailer de arquivo
/// (tipo 9). Todas as linhas têm 240 posições e os campos são posicionais; as posições abaixo estão em base 1,
/// como no manual.
/// </summary>
public static class LeitorCnab240
{
    public static Extrato Ler(string conteudo)
    {
        var linhas = conteudo.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();
        if (linhas.Count == 0) throw new ArquivoInvalidoException("arquivo vazio");
        foreach (var (linha, i) in linhas.Select((l, i) => (l, i)))
            if (linha.Length != 240) throw new ArquivoInvalidoException($"linha {i + 1} tem {linha.Length} posições; esperado 240");

        var header = linhas[0];
        if (Pos(header, 8, 8) != "0") throw new ArquivoInvalidoException("primeira linha não é header de arquivo (tipo 0)");
        var banco = Pos(header, 1, 3);

        string agencia = "", conta = "";
        DateOnly? inicio = null, fim = null;
        decimal? saldoFinal = null;
        var lancamentos = new List<Lancamento>();
        var registrosNoLote = 0;
        var loteAberto = false;

        foreach (var (linha, i) in linhas.Select((l, i) => (l, i)).Skip(1))
        {
            var tipo = Pos(linha, 8, 8);
            switch (tipo)
            {
                case "1":
                    loteAberto = true;
                    registrosNoLote = 1;
                    agencia = Pos(linha, 53, 57).TrimStart('0');
                    conta = Pos(linha, 59, 70).TrimStart('0') + "-" + Pos(linha, 71, 71);
                    inicio = DataDdMmAaaa(Pos(linha, 143, 150)) ?? inicio; // data do saldo inicial
                    break;
                case "3":
                    if (!loteAberto) throw new ArquivoInvalidoException($"linha {i + 1}: segmento fora de lote");
                    registrosNoLote++;
                    var segmento = Pos(linha, 14, 14);
                    if (segmento != "E") continue; // outros segmentos não interessam ao extrato
                    lancamentos.Add(LerSegmentoE(linha, i + 1, lancamentos.Count));
                    break;
                case "5":
                    registrosNoLote++;
                    var declarados = int.Parse(Pos(linha, 18, 23), CultureInfo.InvariantCulture);
                    if (declarados != registrosNoLote)
                        throw new ArquivoInvalidoException($"trailer de lote declara {declarados} registros, mas o lote tem {registrosNoLote}");
                    // saldo final do período: valor (18, 2 decimais) e sinal D/C
                    var saldo = Valor(Pos(linha, 60, 77));
                    saldoFinal = Pos(linha, 78, 78) == "D" ? -saldo : saldo;
                    fim = DataDdMmAaaa(Pos(linha, 79, 86)) ?? fim;
                    loteAberto = false;
                    break;
                case "9":
                    var totalDeclarado = int.Parse(Pos(linha, 24, 29), CultureInfo.InvariantCulture);
                    if (totalDeclarado != linhas.Count)
                        throw new ArquivoInvalidoException($"trailer de arquivo declara {totalDeclarado} registros, mas o arquivo tem {linhas.Count}");
                    break;
                default:
                    throw new ArquivoInvalidoException($"linha {i + 1}: tipo de registro '{tipo}' desconhecido");
            }
        }

        if (linhas[^1][7] != '9') throw new ArquivoInvalidoException("arquivo sem trailer (tipo 9)");
        return new Extrato(banco, agencia, conta, inicio, fim ?? lancamentos.Select(l => (DateOnly?)l.Data).Max(), saldoFinal, lancamentos);
    }

    /// <summary>
    /// Segmento E: 143-150 data do lançamento (DDMMAAAA), 151-168 valor (2 decimais implícitos), 169 D/C,
    /// 170-172 categoria, 173-176 código do histórico, 177-201 descrição do histórico, 202-240 número do documento.
    /// </summary>
    private static Lancamento LerSegmentoE(string linha, int numeroLinha, int indice)
    {
        var data = DataDdMmAaaa(Pos(linha, 143, 150)) ?? throw new ArquivoInvalidoException($"linha {numeroLinha}: data inválida");
        var valor = Valor(Pos(linha, 151, 168));
        var natureza = Pos(linha, 169, 169);
        if (natureza is not ("D" or "C")) throw new ArquivoInvalidoException($"linha {numeroLinha}: natureza '{natureza}' inválida (D ou C)");
        if (natureza == "D") valor = -valor;
        var historico = Pos(linha, 177, 201).Trim();
        var documento = Pos(linha, 202, 240).Trim().TrimStart('0');
        var id = $"{Pos(linha, 9, 13)}-{data:yyyyMMdd}-{indice}";
        return new Lancamento(id, Origem.Banco, data, valor, historico, documento.Length == 0 ? null : documento);
    }

    /// <summary>Recorte posicional em base 1, inclusivo nas duas pontas, como no manual FEBRABAN.</summary>
    internal static string Pos(string linha, int de, int ate) => linha.Substring(de - 1, ate - de + 1);

    internal static decimal Valor(string campo) =>
        long.TryParse(campo, NumberStyles.None, CultureInfo.InvariantCulture, out var centavos) ? centavos / 100m : throw new ArquivoInvalidoException($"valor inválido '{campo}'");

    internal static DateOnly? DataDdMmAaaa(string campo) =>
        DateOnly.TryParseExact(campo, "ddMMyyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) && d.Year > 1900 ? d : null;
}

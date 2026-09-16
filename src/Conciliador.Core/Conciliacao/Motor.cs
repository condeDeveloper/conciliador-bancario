using Conciliador.Core.Modelo;

namespace Conciliador.Core.Conciliacao;

public enum Regra
{
    /// <summary>Mesmo número de documento e mesmo valor.</summary>
    Documento,
    /// <summary>Mesmo valor e mesma data.</summary>
    ValorEData,
    /// <summary>Mesmo valor dentro da janela de dias.</summary>
    ValorNaJanela,
    /// <summary>Um lançamento do banco igual à soma de vários internos na janela (ou o inverso).</summary>
    Agrupamento,
    /// <summary>Mesmo valor e descrições semelhantes, janela ampliada.</summary>
    Descricao,
}

public sealed record Par(Regra Regra, IReadOnlyList<Lancamento> Banco, IReadOnlyList<Lancamento> Interno, double Confianca, string Explicacao)
{
    public decimal Valor => Banco.Sum(l => l.Valor);
}

public sealed record Divergencia(string Tipo, string Descricao, decimal Valor);

public sealed record Resultado(
    IReadOnlyList<Par> Conciliados,
    IReadOnlyList<Lancamento> PendentesBanco,
    IReadOnlyList<Lancamento> PendentesInterno,
    IReadOnlyList<Divergencia> Divergencias,
    Resumo Resumo);

public sealed record Resumo(
    int TotalBanco, int TotalInterno, int Conciliados, int PendentesBanco, int PendentesInterno,
    decimal ValorConciliado, decimal ValorPendenteBanco, decimal ValorPendenteInterno, double Cobertura,
    IReadOnlyDictionary<Regra, int> PorRegra);

public sealed record Parametros(
    int JanelaDias = 3,
    int JanelaDescricaoDias = 10,
    double SimilaridadeMinima = 0.5,
    int MaximoNoAgrupamento = 4,
    decimal? SaldoInternoFinal = null);

/// <summary>
/// Conciliação em cascata: as regras rodam da mais segura para a menos segura, e cada lançamento só é usado uma vez.
/// O que sobra vira pendência de um lado ou do outro, e as divergências resumem o que não fecha.
/// </summary>
public sealed class Motor(Parametros? parametros = null)
{
    public Parametros Parametros { get; } = parametros ?? new Parametros();

    public Resultado Conciliar(Extrato extrato, IReadOnlyList<Lancamento> internos) => Conciliar(extrato.Lancamentos, internos, extrato.SaldoFinal);

    public Resultado Conciliar(IReadOnlyList<Lancamento> banco, IReadOnlyList<Lancamento> internos, decimal? saldoBanco = null)
    {
        var pares = new List<Par>();
        var b = banco.OrderBy(l => l.Data).ThenBy(l => l.Id).ToList();
        var i = internos.OrderBy(l => l.Data).ThenBy(l => l.Id).ToList();

        PorDocumento(b, i, pares);
        PorValor(b, i, pares, Regra.ValorEData, 0);
        PorValor(b, i, pares, Regra.ValorNaJanela, Parametros.JanelaDias);
        PorAgrupamento(b, i, pares, umBancoVariosInternos: true);
        PorAgrupamento(b, i, pares, umBancoVariosInternos: false);
        PorDescricao(b, i, pares);

        var divergencias = new List<Divergencia>();
        var valorPendenteBanco = b.Sum(l => l.Valor);
        var valorPendenteInterno = i.Sum(l => l.Valor);
        if (b.Count > 0) divergencias.Add(new Divergencia("banco-sem-interno", $"{b.Count} lançamento(s) no extrato sem correspondência no sistema", valorPendenteBanco));
        if (i.Count > 0) divergencias.Add(new Divergencia("interno-sem-banco", $"{i.Count} lançamento(s) no sistema sem correspondência no extrato", valorPendenteInterno));
        if (saldoBanco is { } sb && Parametros.SaldoInternoFinal is { } si && sb != si)
            divergencias.Add(new Divergencia("saldo", $"saldo do banco {sb:N2} difere do saldo interno {si:N2}", sb - si));
        var diferenca = valorPendenteBanco - valorPendenteInterno;
        if (diferenca != 0 && (b.Count > 0 || i.Count > 0))
            divergencias.Add(new Divergencia("diferenca-liquida", "diferença líquida entre as pendências dos dois lados", diferenca));

        var conciliadosBanco = pares.Sum(p => p.Banco.Count);
        var resumo = new Resumo(banco.Count, internos.Count, pares.Count, b.Count, i.Count,
            pares.Sum(p => p.Valor), valorPendenteBanco, valorPendenteInterno,
            banco.Count == 0 ? 1 : (double)conciliadosBanco / banco.Count,
            Enum.GetValues<Regra>().ToDictionary(r => r, r => pares.Count(p => p.Regra == r)));
        return new Resultado(pares, b, i, divergencias, resumo);
    }

    private static void PorDocumento(List<Lancamento> banco, List<Lancamento> internos, List<Par> pares)
    {
        foreach (var lb in banco.ToList())
        {
            var docs = Documentos(lb);
            if (docs.Count == 0) continue;
            var li = internos.FirstOrDefault(x => x.Valor == lb.Valor && Documentos(x).Overlaps(docs));
            if (li is null) continue;
            Casar(banco, internos, pares, new Par(Regra.Documento, [lb], [li], 1.0, $"documento {docs.Intersect(Documentos(li)).First()} e valor {lb.Valor:N2}"));
        }
    }

    private void PorValor(List<Lancamento> banco, List<Lancamento> internos, List<Par> pares, Regra regra, int janela)
    {
        foreach (var lb in banco.ToList())
        {
            // entre vários candidatos de mesmo valor, o de data mais próxima; empate desfeito pela descrição
            var li = internos
                .Where(x => x.Valor == lb.Valor && Math.Abs(x.Data.DayNumber - lb.Data.DayNumber) <= janela)
                .OrderBy(x => Math.Abs(x.Data.DayNumber - lb.Data.DayNumber))
                .ThenByDescending(x => Texto.Similaridade(x.Descricao, lb.Descricao))
                .FirstOrDefault();
            if (li is null) continue;
            var dias = Math.Abs(li.Data.DayNumber - lb.Data.DayNumber);
            var confianca = janela == 0 ? 0.95 : 0.85 - 0.03 * dias;
            Casar(banco, internos, pares, new Par(regra, [lb], [li], confianca, dias == 0 ? $"valor {lb.Valor:N2} na mesma data" : $"valor {lb.Valor:N2} com {dias} dia(s) de diferença"));
        }
    }

    private void PorAgrupamento(List<Lancamento> banco, List<Lancamento> internos, List<Par> pares, bool umBancoVariosInternos)
    {
        var alvos = umBancoVariosInternos ? banco : internos;
        var fonte = umBancoVariosInternos ? internos : banco;
        foreach (var alvo in alvos.ToList())
        {
            var candidatos = fonte
                .Where(x => Math.Sign(x.Valor) == Math.Sign(alvo.Valor) && Math.Abs(x.Valor) <= Math.Abs(alvo.Valor) && Math.Abs(x.Data.DayNumber - alvo.Data.DayNumber) <= Parametros.JanelaDias)
                .OrderBy(x => Math.Abs(x.Data.DayNumber - alvo.Data.DayNumber)).Take(12).ToList();
            if (candidatos.Count < 2) continue;
            var grupo = SubconjuntoComSoma(candidatos, alvo.Valor, Parametros.MaximoNoAgrupamento);
            if (grupo is null) continue;
            var par = umBancoVariosInternos
                ? new Par(Regra.Agrupamento, [alvo], grupo, 0.75, $"{grupo.Count} lançamentos internos somam {alvo.Valor:N2}")
                : new Par(Regra.Agrupamento, grupo, [alvo], 0.75, $"{grupo.Count} lançamentos do banco somam {alvo.Valor:N2}");
            Casar(banco, internos, pares, par);
        }
    }

    private void PorDescricao(List<Lancamento> banco, List<Lancamento> internos, List<Par> pares)
    {
        foreach (var lb in banco.ToList())
        {
            var melhor = internos
                .Where(x => x.Valor == lb.Valor && Math.Abs(x.Data.DayNumber - lb.Data.DayNumber) <= Parametros.JanelaDescricaoDias)
                .Select(x => (Lanc: x, Sim: Texto.Similaridade(x.Descricao, lb.Descricao)))
                .Where(t => t.Sim >= Parametros.SimilaridadeMinima)
                .OrderByDescending(t => t.Sim).FirstOrDefault();
            if (melhor.Lanc is null) continue;
            Casar(banco, internos, pares, new Par(Regra.Descricao, [lb], [melhor.Lanc], 0.5 + 0.3 * melhor.Sim, $"descrições {melhor.Sim:P0} semelhantes e valor {lb.Valor:N2}"));
        }
    }

    /// <summary>Busca em profundidade limitada por um subconjunto (de 2 a máximo itens) cuja soma é exatamente o alvo.</summary>
    public static List<Lancamento>? SubconjuntoComSoma(List<Lancamento> itens, decimal alvo, int maximo)
    {
        var escolhidos = new List<Lancamento>();
        return Buscar(0, alvo) ? escolhidos : null;

        bool Buscar(int inicio, decimal restante)
        {
            if (restante == 0) return escolhidos.Count >= 2;
            if (escolhidos.Count >= maximo) return false;
            for (var k = inicio; k < itens.Count; k++)
            {
                if (Math.Abs(itens[k].Valor) > Math.Abs(restante)) continue;
                escolhidos.Add(itens[k]);
                if (Buscar(k + 1, restante - itens[k].Valor)) return true;
                escolhidos.RemoveAt(escolhidos.Count - 1);
            }
            return false;
        }
    }

    private static HashSet<string> Documentos(Lancamento l)
    {
        var docs = new HashSet<string>(Texto.Numeros(l.Descricao), StringComparer.Ordinal);
        if (l.Documento is { } d) { var limpo = d.TrimStart('0'); if (limpo.Length > 0) docs.Add(limpo); }
        return docs;
    }

    private static void Casar(List<Lancamento> banco, List<Lancamento> internos, List<Par> pares, Par par)
    {
        foreach (var l in par.Banco) banco.Remove(l);
        foreach (var l in par.Interno) internos.Remove(l);
        pares.Add(par);
    }
}

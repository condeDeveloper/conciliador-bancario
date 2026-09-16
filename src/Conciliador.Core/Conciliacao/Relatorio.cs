using System.Globalization;
using System.Text;
using Conciliador.Core.Modelo;

namespace Conciliador.Core.Conciliacao;

/// <summary>Relatório em texto simples, pronto para o terminal ou para anexar num e-mail.</summary>
public static class Relatorio
{
    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");

    public static string Texto(Resultado r)
    {
        var sb = new StringBuilder();
        var s = r.Resumo;
        sb.AppendLine("CONCILIAÇÃO BANCÁRIA");
        sb.AppendLine(new string('=', 72));
        sb.AppendLine($"Extrato: {s.TotalBanco} lançamentos   Sistema: {s.TotalInterno} lançamentos");
        sb.AppendLine($"Conciliados: {s.Conciliados} pares ({s.Cobertura:P0} do extrato)   Valor: {Moeda(s.ValorConciliado)}");
        sb.AppendLine($"Pendentes no extrato: {s.PendentesBanco} ({Moeda(s.ValorPendenteBanco)})   Pendentes no sistema: {s.PendentesInterno} ({Moeda(s.ValorPendenteInterno)})");
        sb.AppendLine();
        sb.AppendLine("Por regra: " + string.Join(", ", s.PorRegra.Where(kv => kv.Value > 0).Select(kv => $"{kv.Key} {kv.Value}")));
        sb.AppendLine();

        if (r.Conciliados.Count > 0)
        {
            sb.AppendLine("CONCILIADOS");
            sb.AppendLine(new string('-', 72));
            foreach (var p in r.Conciliados)
            {
                sb.AppendLine($"[{p.Regra,-14}] {Moeda(p.Valor),16}  conf. {p.Confianca:P0}  {p.Explicacao}");
                foreach (var l in p.Banco) sb.AppendLine($"    banco    {Linha(l)}");
                foreach (var l in p.Interno) sb.AppendLine($"    sistema  {Linha(l)}");
            }
            sb.AppendLine();
        }
        Secao(sb, "PENDENTES NO EXTRATO (não encontrados no sistema)", r.PendentesBanco);
        Secao(sb, "PENDENTES NO SISTEMA (não encontrados no extrato)", r.PendentesInterno);
        if (r.Divergencias.Count > 0)
        {
            sb.AppendLine("DIVERGÊNCIAS");
            sb.AppendLine(new string('-', 72));
            foreach (var d in r.Divergencias) sb.AppendLine($"{d.Tipo,-20} {Moeda(d.Valor),16}  {d.Descricao}");
        }
        return sb.ToString();
    }

    private static void Secao(StringBuilder sb, string titulo, IReadOnlyList<Lancamento> itens)
    {
        if (itens.Count == 0) return;
        sb.AppendLine(titulo);
        sb.AppendLine(new string('-', 72));
        foreach (var l in itens) sb.AppendLine("    " + Linha(l));
        sb.AppendLine();
    }

    private static string Linha(Lancamento l) => $"{l.Data:dd/MM/yyyy} {Moeda(l.Valor),16}  {l.Descricao}{(l.Documento is null ? "" : $"  doc {l.Documento}")}  ({l.Id})";

    private static string Moeda(decimal v) => v.ToString("C2", Br);
}

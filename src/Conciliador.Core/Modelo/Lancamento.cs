namespace Conciliador.Core.Modelo;

public enum Origem { Banco, Interno }

/// <summary>Um movimento financeiro normalizado, venha do extrato do banco (OFX, CNAB) ou do sistema interno (CSV).</summary>
public sealed record Lancamento(
    string Id,
    Origem Origem,
    DateOnly Data,
    decimal Valor,
    string Descricao,
    string? Documento = null)
{
    /// <summary>Crédito quando positivo, débito quando negativo.</summary>
    public bool Credito => Valor > 0;
}

/// <summary>Extrato lido de um arquivo: cabeçalho da conta, lançamentos e saldo final informado pelo banco.</summary>
public sealed record Extrato(
    string Banco,
    string Agencia,
    string Conta,
    DateOnly? Inicio,
    DateOnly? Fim,
    decimal? SaldoFinal,
    IReadOnlyList<Lancamento> Lancamentos)
{
    public decimal Movimento => Lancamentos.Sum(l => l.Valor);
}

public sealed class ArquivoInvalidoException(string mensagem) : Exception(mensagem);

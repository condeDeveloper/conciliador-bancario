using Conciliador.Core.Conciliacao;
using Conciliador.Core.Modelo;

namespace Conciliador.Tests;

public class MotorTests
{
    private static Lancamento B(string id, int dia, decimal valor, string desc, string? doc = null) => new(id, Origem.Banco, new DateOnly(2026, 9, dia), valor, desc, doc);
    private static Lancamento I(string id, int dia, decimal valor, string desc, string? doc = null) => new(id, Origem.Interno, new DateOnly(2026, 9, dia), valor, desc, doc);

    [Fact]
    public void CascataUsaARegraMaisSeguraDisponivel()
    {
        var banco = new[]
        {
            B("b1", 1, -1250m, "PAGTO FORNECEDOR ALFA", "000123"),
            B("b2", 2, 3200m, "TED RECEBIDA CLIENTE BETA"),
            B("b3", 5, -500m, "BOLETO ENERGIA"),
            B("b4", 10, -700m, "PIX ENVIADO CONSULTORIA GAMA LTDA"),
        };
        var internos = new[]
        {
            I("i1", 3, -1250m, "Fornecedor Alfa NF 123", "123"),
            I("i2", 2, 3200m, "Recebimento cliente Beta"),
            I("i3", 3, -500m, "Conta de energia setembro"),
            I("i4", 18, -700m, "Consultoria Gama - honorários"),
        };
        var r = new Motor().Conciliar(banco, internos);
        r.Conciliados.Should().HaveCount(4);
        r.Conciliados.Single(p => p.Banco[0].Id == "b1").Regra.Should().Be(Regra.Documento, "documento bate apesar da data diferente");
        r.Conciliados.Single(p => p.Banco[0].Id == "b2").Regra.Should().Be(Regra.ValorEData);
        r.Conciliados.Single(p => p.Banco[0].Id == "b3").Regra.Should().Be(Regra.ValorNaJanela);
        r.Conciliados.Single(p => p.Banco[0].Id == "b4").Regra.Should().Be(Regra.Descricao, "8 dias de diferença só passa pela descrição");
        r.PendentesBanco.Should().BeEmpty();
        r.PendentesInterno.Should().BeEmpty();
        r.Divergencias.Should().BeEmpty();
        r.Resumo.Cobertura.Should().Be(1.0);
        r.Resumo.PorRegra[Regra.Documento].Should().Be(1);
    }

    [Fact]
    public void EntreCandidatosDeMesmoValorEscolheDataMaisProximaEDepoisDescricao()
    {
        var banco = new[] { B("b1", 5, -100m, "PAGTO ALUGUEL SALA") };
        var internos = new[] { I("longe", 8, -100m, "Aluguel sala"), I("perto", 4, -100m, "Outra coisa") };
        new Motor().Conciliar(banco, internos).Conciliados.Single().Interno[0].Id.Should().Be("perto");

        var empate = new[] { I("a", 4, -100m, "Material de escritório"), I("b", 4, -100m, "Aluguel da sala") };
        new Motor().Conciliar(banco, empate).Conciliados.Single().Interno[0].Id.Should().Be("b");
    }

    [Fact]
    public void AgrupaVariosInternosNumSoDoBanco()
    {
        var banco = new[] { B("b1", 5, -1000m, "PAGTO LOTE FORNECEDORES") };
        var internos = new[] { I("i1", 5, -300m, "NF 1"), I("i2", 5, -250m, "NF 2"), I("i3", 4, -450m, "NF 3"), I("i4", 5, -999m, "outro") };
        var r = new Motor().Conciliar(banco, internos);
        var par = r.Conciliados.Should().ContainSingle().Subject;
        par.Regra.Should().Be(Regra.Agrupamento);
        par.Interno.Select(l => l.Id).Should().BeEquivalentTo("i1", "i2", "i3");
        par.Valor.Should().Be(-1000m);
        r.PendentesInterno.Should().ContainSingle().Which.Id.Should().Be("i4");
    }

    [Fact]
    public void AgrupaVariosDoBancoNumSoInterno()
    {
        var banco = new[] { B("b1", 1, 400m, "PIX RECEBIDO"), B("b2", 1, 600m, "PIX RECEBIDO") };
        var internos = new[] { I("i1", 1, 1000m, "Venda 555") };
        var r = new Motor().Conciliar(banco, internos);
        r.Conciliados.Should().ContainSingle().Which.Banco.Should().HaveCount(2);
        r.Resumo.Cobertura.Should().Be(1.0);
    }

    [Fact]
    public void SubconjuntoComSomaRespeitaMaximoENaoAceitaUmSo()
    {
        var itens = new List<Lancamento> { I("a", 1, 10m, ""), I("b", 1, 20m, ""), I("c", 1, 30m, ""), I("d", 1, 40m, "") };
        var sessenta = Motor.SubconjuntoComSoma(itens, 60m, 4)!;
        sessenta.Sum(l => l.Valor).Should().Be(60m);
        sessenta.Count.Should().BeGreaterThanOrEqualTo(2);
        Motor.SubconjuntoComSoma(itens, 100m, 4)!.Should().HaveCount(4);
        Motor.SubconjuntoComSoma(itens, 100m, 3).Should().BeNull();
        Motor.SubconjuntoComSoma(itens, 10m, 4).Should().BeNull("um item só não é agrupamento");
        Motor.SubconjuntoComSoma(itens, 7m, 4).Should().BeNull();
    }

    [Fact]
    public void PendenciasEDivergenciasDeSaldo()
    {
        var banco = new[] { B("b1", 1, -50m, "TARIFA"), B("b2", 2, 100m, "DEPOSITO") };
        var internos = new[] { I("i1", 2, 100m, "Depósito"), I("i2", 3, -80m, "Compra") };
        var motor = new Motor(new Parametros(SaldoInternoFinal: 1000m));
        var r = motor.Conciliar(banco, internos, saldoBanco: 950m);
        r.Conciliados.Should().ContainSingle();
        r.PendentesBanco.Should().ContainSingle().Which.Id.Should().Be("b1");
        r.PendentesInterno.Should().ContainSingle().Which.Id.Should().Be("i2");
        r.Divergencias.Select(d => d.Tipo).Should().BeEquivalentTo("banco-sem-interno", "interno-sem-banco", "saldo", "diferenca-liquida");
        r.Divergencias.Single(d => d.Tipo == "saldo").Valor.Should().Be(-50m);
        r.Divergencias.Single(d => d.Tipo == "diferenca-liquida").Valor.Should().Be(30m);
        r.Resumo.Cobertura.Should().Be(0.5);
        r.Resumo.ValorPendenteBanco.Should().Be(-50m);
    }

    [Fact]
    public void NaoCasaSinaisOpostosNemValoresDiferentes()
    {
        var banco = new[] { B("b1", 1, 100m, "X") };
        var internos = new[] { I("i1", 1, -100m, "X"), I("i2", 1, 100.01m, "X") };
        var r = new Motor().Conciliar(banco, internos);
        r.Conciliados.Should().BeEmpty();
        r.PendentesBanco.Should().HaveCount(1);
        r.PendentesInterno.Should().HaveCount(2);
    }

    [Fact]
    public void SimilaridadeDeTextoIgnoraRuidoAcentosENumeros()
    {
        Texto.Tokens("PAGTO  Fornecedor ALFA LTDA 000123").Should().BeEquivalentTo("fornecedor", "alfa");
        Texto.Similaridade("Consultoria Gama - honorários", "PIX ENVIADO CONSULTORIA GAMA LTDA").Should().BeGreaterThanOrEqualTo(0.5);
        Texto.Similaridade("Aluguel", "Energia").Should().Be(0);
        Texto.Similaridade("", "x").Should().Be(0);
        Texto.Numeros("NF 000123 e boleto 23790123456789").Should().BeEquivalentTo("123", "23790123456789");
    }

    [Fact]
    public void RelatorioEmTextoListaTudo()
    {
        var banco = new[] { B("b1", 1, -50m, "TARIFA"), B("b2", 2, 100m, "DEPOSITO") };
        var internos = new[] { I("i1", 2, 100m, "Depósito") };
        var r = new Motor().Conciliar(banco, internos);
        var texto = Relatorio.Texto(r);
        texto.Should().Contain("CONCILIAÇÃO BANCÁRIA").And.Contain("CONCILIADOS").And.Contain("PENDENTES NO EXTRATO").And.Contain("DIVERGÊNCIAS");
        texto.Should().Contain("ValorEData").And.Contain("(b1)").And.Contain("banco-sem-interno");
        texto.Should().NotContain("PENDENTES NO SISTEMA");
    }
}

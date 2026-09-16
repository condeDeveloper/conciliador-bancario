using Conciliador.Core.Leitores;
using Conciliador.Core.Modelo;

namespace Conciliador.Tests;

public class LeitoresTests
{
    public const string OfxSgml = """
        OFXHEADER:100
        DATA:OFXSGML
        VERSION:102
        SECURITY:NONE
        ENCODING:USASCII
        CHARSET:1252
        COMPRESSION:NONE
        OLDFILEUID:NONE
        NEWFILEUID:NONE

        <OFX>
        <SIGNONMSGSRSV1><SONRS><STATUS><CODE>0<SEVERITY>INFO</STATUS><DTSERVER>20260903120000[-3:BRT]<LANGUAGE>POR</SONRS></SIGNONMSGSRSV1>
        <BANKMSGSRSV1><STMTTRNRS><TRNUID>1<STATUS><CODE>0<SEVERITY>INFO</STATUS>
        <STMTRS><CURDEF>BRL
        <BANKACCTFROM><BANKID>0341<BRANCHID>1234<ACCTID>56789-0<ACCTTYPE>CHECKING</BANKACCTFROM>
        <BANKTRANLIST><DTSTART>20260901<DTEND>20260903
        <STMTTRN><TRNTYPE>DEBIT<DTPOSTED>20260901120000[-3:BRT]<TRNAMT>-1250.00<FITID>2026090100001<CHECKNUM>000123<MEMO>PAGTO FORNECEDOR ALFA</STMTTRN>
        <STMTTRN><TRNTYPE>CREDIT<DTPOSTED>20260902<TRNAMT>3200,00<FITID>2026090200002<NAME>TED RECEBIDA<MEMO>CLIENTE BETA</STMTTRN>
        <STMTTRN><TRNTYPE>DEBIT<DTPOSTED>20260903<TRNAMT>-89.90<FITID>2026090300003<MEMO>TARIFA PACOTE SERVICOS</STMTTRN>
        <STMTTRN><TRNTYPE>DEBIT<DTPOSTED>20260903<TRNAMT>-89.90<FITID>2026090300003<MEMO>TARIFA PACOTE SERVICOS</STMTTRN>
        </BANKTRANLIST>
        <LEDGERBAL><BALAMT>10860.10<DTASOF>20260903</LEDGERBAL>
        </STMTRS></STMTTRNRS></BANKMSGSRSV1>
        </OFX>
        """;

    [Fact]
    public void LeOfxSgmlComTagsSemFechamento()
    {
        var e = LeitorOfx.Ler(OfxSgml);
        e.Banco.Should().Be("0341");
        e.Agencia.Should().Be("1234");
        e.Conta.Should().Be("56789-0");
        e.Inicio.Should().Be(new DateOnly(2026, 9, 1));
        e.Fim.Should().Be(new DateOnly(2026, 9, 3));
        e.SaldoFinal.Should().Be(10860.10m);
        e.Lancamentos.Should().HaveCount(3, "FITID repetido é ignorado");
        e.Lancamentos[0].Should().BeEquivalentTo(new Lancamento("2026090100001", Origem.Banco, new DateOnly(2026, 9, 1), -1250m, "PAGTO FORNECEDOR ALFA", "000123"));
        e.Lancamentos[1].Valor.Should().Be(3200m, "vírgula decimal é aceita");
        e.Lancamentos[1].Descricao.Should().Be("TED RECEBIDA CLIENTE BETA");
        e.Lancamentos[1].Documento.Should().BeNull();
        e.Movimento.Should().Be(1860.10m);
    }

    [Fact]
    public void LeOfxXml()
    {
        var xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <?OFX OFXHEADER="200" VERSION="211" SECURITY="NONE"?>
            <OFX><BANKMSGSRSV1><STMTTRNRS><STMTRS><CURDEF>BRL</CURDEF>
            <BANKACCTFROM><BANKID>001</BANKID><ACCTID>12345</ACCTID></BANKACCTFROM>
            <BANKTRANLIST><DTSTART>20260801</DTSTART><DTEND>20260831</DTEND>
            <STMTTRN><TRNTYPE>CREDIT</TRNTYPE><DTPOSTED>20260815</DTPOSTED><TRNAMT>100.50</TRNAMT><FITID>a</FITID><MEMO>Pix recebido</MEMO></STMTTRN>
            </BANKTRANLIST><LEDGERBAL><BALAMT>-5.25</BALAMT><DTASOF>20260831</DTASOF></LEDGERBAL>
            </STMTRS></STMTTRNRS></BANKMSGSRSV1></OFX>
            """;
        var e = LeitorOfx.Ler(xml);
        e.Banco.Should().Be("001");
        e.Lancamentos.Should().ContainSingle().Which.Valor.Should().Be(100.50m);
        e.SaldoFinal.Should().Be(-5.25m);
    }

    [Fact]
    public void RejeitaOfxInvalido()
    {
        var a = () => LeitorOfx.Ler("isso não é ofx");
        a.Should().Throw<ArquivoInvalidoException>();
        var b = () => LeitorOfx.Ler("<OFX><STMTTRN><DTPOSTED>20260101</STMTTRN></OFX>");
        b.Should().Throw<ArquivoInvalidoException>().WithMessage("*TRNAMT*");
    }

    private static readonly List<Lancamento> Lancs =
    [
        new("x", Origem.Banco, new DateOnly(2026, 9, 1), -1250.00m, "PAGTO FORNECEDOR ALFA", "123"),
        new("y", Origem.Banco, new DateOnly(2026, 9, 2), 3200.00m, "TED RECEBIDA CLIENTE BETA"),
        new("z", Origem.Banco, new DateOnly(2026, 9, 3), -89.90m, "TARIFA PACOTE SERVIÇOS"),
    ];

    [Fact]
    public void GeraELeCnab240()
    {
        var arquivo = GeradorCnab240.Gerar("341", "1234", "56789", "0", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 3), -10_860.10m, Lancs);
        var linhas = arquivo.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        linhas.Should().HaveCount(7, "header, header de lote, 3 segmentos E, trailer de lote e trailer de arquivo").And.OnlyContain(l => l.Length == 240);
        linhas[0][7].Should().Be('0');
        linhas[^1][7].Should().Be('9');

        var e = LeitorCnab240.Ler(arquivo);
        e.Banco.Should().Be("341");
        e.Agencia.Should().Be("1234");
        e.Conta.Should().Be("56789-0");
        e.Inicio.Should().Be(new DateOnly(2026, 9, 1));
        e.Fim.Should().Be(new DateOnly(2026, 9, 3));
        e.SaldoFinal.Should().Be(-10_860.10m, "sinal D no trailer");
        e.Lancamentos.Should().HaveCount(3);
        e.Lancamentos[0].Valor.Should().Be(-1250m);
        e.Lancamentos[0].Documento.Should().Be("123");
        e.Lancamentos[1].Valor.Should().Be(3200m);
        e.Lancamentos[1].Documento.Should().BeNull();
        e.Lancamentos[2].Descricao.Should().Be("TARIFA PACOTE SERVICOS", "sem acento e em maiúsculas no CNAB");
        e.Lancamentos.Select(l => l.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void CnabValidaTamanhoTrailersENatureza()
    {
        var ok = GeradorCnab240.Gerar("341", "1", "2", "3", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 3), 0m, Lancs);
        var linhas = ok.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).ToList();

        var curta = string.Join("\r\n", linhas.Select((l, i) => i == 2 ? l[..239] : l));
        var a = () => LeitorCnab240.Ler(curta);
        a.Should().Throw<ArquivoInvalidoException>().WithMessage("*239 posições*");

        var semUmSegmento = string.Join("\r\n", linhas.Where((_, i) => i != 3));
        var b = () => LeitorCnab240.Ler(semUmSegmento);
        b.Should().Throw<ArquivoInvalidoException>().WithMessage("*trailer de lote declara*");

        var naturezaErrada = string.Join("\r\n", linhas.Select((l, i) => i == 2 ? l[..168] + "X" + l[169..] : l));
        var c = () => LeitorCnab240.Ler(naturezaErrada);
        c.Should().Throw<ArquivoInvalidoException>().WithMessage("*natureza*");

        var semTrailer = string.Join("\r\n", linhas.Take(5));
        var d = () => LeitorCnab240.Ler(semTrailer);
        d.Should().Throw<ArquivoInvalidoException>();
    }

    [Fact]
    public void LeCsvInternoComVariacoes()
    {
        var csv = """
            id;data;valor;descricao;documento
            CP-1;01/09/2026;-1.250,00;"Fornecedor Alfa; NF 123";123
            CR-7;2026-09-02;3200.00;Cliente Beta "duplicata";
            CP-2;03/09/2026;R$ -45,00;Tarifa bancária
            """;
        var l = LeitorCsvInterno.Ler(csv);
        l.Should().HaveCount(3);
        l[0].Valor.Should().Be(-1250m);
        l[0].Descricao.Should().Be("Fornecedor Alfa; NF 123");
        l[0].Documento.Should().Be("123");
        l[1].Data.Should().Be(new DateOnly(2026, 9, 2));
        l[1].Descricao.Should().Be("Cliente Beta \"duplicata\"");
        l[1].Documento.Should().BeNull();
        l[2].Valor.Should().Be(-45m);
        l[2].Documento.Should().BeNull("coluna ausente na linha");
        l.Should().OnlyContain(x => x.Origem == Origem.Interno);

        var virgula = "id,data,valor,descricao\nA,01/01/2026,10.5,teste\n";
        LeitorCsvInterno.Ler(virgula).Should().ContainSingle().Which.Valor.Should().Be(10.5m);

        var semColuna = () => LeitorCsvInterno.Ler("id;data;descricao\nA;01/01/2026;x\n");
        semColuna.Should().Throw<ArquivoInvalidoException>().WithMessage("*valor*");
        var dataRuim = () => LeitorCsvInterno.Ler("id;data;valor;descricao\nA;31/02/2026;1;x\n");
        dataRuim.Should().Throw<ArquivoInvalidoException>().WithMessage("*data inválida*");
    }
}

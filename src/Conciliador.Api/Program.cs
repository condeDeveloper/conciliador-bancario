using System.Text.Json.Serialization;
using Conciliador.Core.Conciliacao;
using Conciliador.Core.Leitores;
using Conciliador.Core.Modelo;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o => o.SwaggerDoc("v1", new OpenApiInfo
{
    Title = "Conciliador Bancário",
    Version = "v1",
    Description = "Lê extratos OFX e CNAB 240, lançamentos internos em CSV, e concilia em cascata: documento, valor e data, janela, agrupamento e descrição.",
}));

var app = builder.Build();
app.Use(async (ctx, next) =>
{
    try { await next(); }
    catch (ArquivoInvalidoException e) { ctx.Response.StatusCode = 422; await ctx.Response.WriteAsJsonAsync(new { title = e.Message, status = 422 }); }
    catch (BadHttpRequestException e) { ctx.Response.StatusCode = 400; await ctx.Response.WriteAsJsonAsync(new { title = e.Message, status = 400 }); }
});
app.UseSwagger();
app.UseSwaggerUI(o => { o.RoutePrefix = "docs"; o.DocumentTitle = "Conciliador Bancário"; });

var g = app.MapGroup("/api").WithTags("Conciliação");

g.MapPost("/extratos/ler", (LerExtratoRequest req) => Results.Ok(LerExtrato(req.Formato, req.Conteudo)))
    .WithSummary("Lê um extrato OFX ou CNAB 240 e devolve os lançamentos normalizados");

g.MapPost("/conciliacoes", (ConciliarRequest req) =>
{
    var extrato = LerExtrato(req.Formato, req.Extrato);
    var internos = LeitorCsvInterno.Ler(req.Interno);
    var motor = new Motor(req.Parametros ?? new Parametros());
    var resultado = motor.Conciliar(extrato, internos);
    return Results.Ok(new { extrato = new { extrato.Banco, extrato.Agencia, extrato.Conta, extrato.Inicio, extrato.Fim, extrato.SaldoFinal }, resultado, relatorio = Relatorio.Texto(resultado) });
}).WithSummary("Concilia um extrato (OFX ou CNAB 240) com os lançamentos internos (CSV) e devolve pares, pendências, divergências e relatório");

g.MapGet("/exemplo", () =>
{
    var lancs = new List<Lancamento>
    {
        new("b1", Origem.Banco, new DateOnly(2026, 9, 1), -1250.00m, "PAGTO FORNECEDOR ALFA", "000123"),
        new("b2", Origem.Banco, new DateOnly(2026, 9, 2), 3200.00m, "TED RECEBIDA CLIENTE BETA"),
        new("b3", Origem.Banco, new DateOnly(2026, 9, 3), -89.90m, "TARIFA PACOTE SERVICOS"),
    };
    var cnab = GeradorCnab240.Gerar("341", "1234", "56789", "0", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 3), 10_860.10m, lancs);
    var csv = "id;data;valor;descricao;documento\r\nCP-1;01/09/2026;-1250,00;Fornecedor Alfa NF 123;123\r\nCR-7;02/09/2026;3200,00;Cliente Beta duplicata;\r\nCP-2;03/09/2026;-45,00;Tarifa bancária;\r\n";
    return Results.Ok(new ConciliarRequest("cnab240", cnab, csv, null));
}).WithSummary("Um pedido de exemplo pronto para colar no POST /api/conciliacoes");

app.MapGet("/saude", () => Results.Ok(new { status = "ok" }));
app.Run();

static Extrato LerExtrato(string formato, string conteudo) => formato.ToLowerInvariant() switch
{
    "ofx" => LeitorOfx.Ler(conteudo),
    "cnab240" or "cnab" => LeitorCnab240.Ler(conteudo),
    _ => throw new ArquivoInvalidoException($"formato '{formato}' desconhecido; use ofx ou cnab240"),
};

public sealed record LerExtratoRequest(string Formato, string Conteudo);
public sealed record ConciliarRequest(string Formato, string Extrato, string Interno, Parametros? Parametros);

public partial class Program { }

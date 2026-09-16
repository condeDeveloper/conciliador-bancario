using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Conciliador.Tests;

public class ApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;

    public ApiTests(WebApplicationFactory<Program> f) => _http = f.CreateClient();

    [Fact]
    public async Task ExemploConciliaDePontaAPonta()
    {
        var exemplo = await _http.GetFromJsonAsync<JsonElement>("/api/exemplo", Json);
        exemplo.GetProperty("formato").GetString().Should().Be("cnab240");

        var r = await _http.PostAsJsonAsync("/api/conciliacoes", exemplo);
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var corpo = await r.Content.ReadFromJsonAsync<JsonElement>(Json);
        corpo.GetProperty("extrato").GetProperty("banco").GetString().Should().Be("341");
        var resultado = corpo.GetProperty("resultado");
        resultado.GetProperty("conciliados").GetArrayLength().Should().Be(2);
        resultado.GetProperty("pendentesBanco").GetArrayLength().Should().Be(1, "tarifa de 89,90 não existe no sistema");
        resultado.GetProperty("pendentesInterno").GetArrayLength().Should().Be(1, "tarifa de 45,00 não existe no extrato");
        resultado.GetProperty("conciliados")[0].GetProperty("regra").GetString().Should().Be("Documento");
        corpo.GetProperty("relatorio").GetString().Should().Contain("CONCILIAÇÃO BANCÁRIA");
    }

    [Fact]
    public async Task LeOfxEValidaFormato()
    {
        var r = await _http.PostAsJsonAsync("/api/extratos/ler", new { formato = "ofx", conteudo = LeitoresTests.OfxSgml });
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var e = await r.Content.ReadFromJsonAsync<JsonElement>(Json);
        e.GetProperty("lancamentos").GetArrayLength().Should().Be(3);

        var ruim = await _http.PostAsJsonAsync("/api/extratos/ler", new { formato = "xls", conteudo = "x" });
        ruim.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var invalido = await _http.PostAsJsonAsync("/api/extratos/ler", new { formato = "cnab240", conteudo = "linha curta" });
        invalido.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}

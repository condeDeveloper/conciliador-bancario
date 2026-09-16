# Conciliador Bancário

[![CI](https://github.com/condeDeveloper/conciliador-bancario/actions/workflows/ci.yml/badge.svg)](https://github.com/condeDeveloper/conciliador-bancario/actions/workflows/ci.yml)

Conciliação bancária em C# e .NET 8: lê o extrato do banco em **OFX** ou **CNAB 240**, os lançamentos do sistema interno em **CSV**, e casa os dois lados com uma cascata de regras. Devolve os pares conciliados com a regra e a confiança de cada um, as pendências de cada lado, as divergências e um relatório em texto.

## Leitores

- **OFX 1.x e 2.x**: o formato SGML antigo (tags sem fechamento) e o XML. Extrai conta, período, saldo (`LEDGERBAL`) e cada `STMTTRN`; aceita vírgula decimal e ignora `FITID` repetido.
- **CNAB 240** (extrato para conciliação FEBRABAN): header de arquivo, header de lote, **segmento E** por lançamento, trailers com validação da contagem de registros e do saldo final com sinal. Todas as posições em base 1, como no manual, e um **gerador** que produz o arquivo a partir de lançamentos, usado nos testes de ida e volta.
- **CSV interno**: `id;data;valor;descricao;documento`, com separador detectado, datas em `dd/MM/yyyy` ou ISO, valores em `1.234,56` ou `1234.56`, aspas e `R$` tolerados.

## Cascata de conciliação

Cada lançamento é usado uma única vez. As regras rodam da mais segura para a menos segura:

| Ordem | Regra | Quando casa | Confiança |
|---|---|---|---|
| 1 | Documento | mesmo número de documento (campo ou número longo na descrição) e mesmo valor | 100% |
| 2 | Valor e data | mesmo valor na mesma data; empate desfeito pela descrição | 95% |
| 3 | Valor na janela | mesmo valor até N dias de diferença (padrão 3) | 85% menos 3% por dia |
| 4 | Agrupamento | um lançamento do banco igual à soma de 2 a 4 internos na janela, ou o inverso | 75% |
| 5 | Descrição | mesmo valor, até 10 dias, descrições com similaridade de Jaccard ≥ 0,5 sobre tokens normalizados | 50% a 80% |

O que sobra vira pendência do banco ou do sistema. Divergências: pendências de cada lado, diferença líquida e saldo do banco versus saldo interno.

## Rodar

```bash
dotnet run --project src/Conciliador.Api
```

Documentação em http://localhost:5000/docs. O `GET /api/exemplo` devolve um pedido completo (CNAB gerado e CSV) pronto para colar no `POST /api/conciliacoes`:

```bash
curl -s localhost:5000/api/exemplo | curl -s localhost:5000/api/conciliacoes -H 'Content-Type: application/json' -d @- | jq -r .relatorio
```

```
CONCILIAÇÃO BANCÁRIA
========================================================================
Extrato: 3 lançamentos   Sistema: 3 lançamentos
Conciliados: 2 pares (67% do extrato)   Valor: R$ 1.950,00
Pendentes no extrato: 1 (-R$ 89,90)   Pendentes no sistema: 1 (-R$ 45,00)

Por regra: Documento 1, ValorEData 1
...
```

## Testes

```bash
dotnet test
```

OFX SGML e XML, FITID duplicado, CNAB gerado e lido de volta com validação de tamanho, trailers e natureza, CSV com aspas e formatos, cada regra da cascata, desempate por data e descrição, agrupamento nos dois sentidos, busca de subconjunto, divergências de saldo, similaridade de texto, relatório e API de ponta a ponta.

## Arquitetura

```
src/Conciliador.Core
  Modelo/       Lancamento, Extrato
  Leitores/     LeitorOfx, LeitorCnab240, GeradorCnab240, LeitorCsvInterno
  Conciliacao/  Motor (cascata), Texto (normalização e Jaccard), Relatorio
src/Conciliador.Api  minimal API com Swagger
tests/Conciliador.Tests
```

## Licença

MIT

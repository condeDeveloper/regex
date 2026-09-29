using System.Text;
using Dotnet = System.Text.RegularExpressions;

namespace Conde.Regex.Testes;

/// <summary>
/// O juiz: o <c>System.Text.RegularExpressions</c> do .NET, rodando de verdade.
/// </summary>
/// <remarks>
/// <para>
/// A comparação é separada em duas, e a separação é o resultado mais
/// interessante deste projeto.
/// </para>
/// <para>
/// <b>A correspondência</b> — se casou, onde, e o quê — tem de bater sempre, e
/// bate: 100% em todos os corpora. É o que qualquer código que use a biblioteca
/// enxerga primeiro.
/// </para>
/// <para>
/// <b>As capturas</b> batem em quase tudo, e há uma família de casos em que não
/// batem — sempre a mesma. Quando a última volta de um laço casa o vazio, um
/// motor com retrocesso registra essa volta e um Thompson não. As duas respostas
/// estão certas dentro da própria semântica, e a diferença é <b>de
/// arquitetura</b>: não é um defeito para corrigir, é o preço de não ter
/// retrocesso. Está medida, nomeada e no README.
/// </para>
/// </remarks>
public static class Juiz
{
    public sealed record Resultado(
        bool CorrespondenciaBate,
        bool CapturasBatem,
        string? Queixa);

    public static Resultado Comparar(string expressao, string texto)
    {
        var meu = new Expressao(expressao).Procurar(texto);
        var dele = new Dotnet.Regex(expressao).Match(texto);

        if (meu.Sucesso != dele.Success)
        {
            return new Resultado(false, false,
                $"/{expressao}/ em {Mostrar(texto)}: eu digo {meu.Sucesso}, "
                + $"o .NET diz {dele.Success}");
        }

        if (!dele.Success)
        {
            return new Resultado(true, true, null);
        }

        if (meu.Inicio != dele.Index || meu.Valor != dele.Value)
        {
            return new Resultado(false, false,
                $"/{expressao}/ em {Mostrar(texto)}: peguei {Mostrar(meu.Valor)} "
                + $"em {meu.Inicio}, o .NET pegou {Mostrar(dele.Value)} em {dele.Index}");
        }

        for (var i = 1; i < dele.Groups.Count; i++)
        {
            var meuGrupo = meu[i];
            var deleGrupo = dele.Groups[i];

            if (meuGrupo.Sucesso != deleGrupo.Success
                || (deleGrupo.Success && meuGrupo.Valor != deleGrupo.Value))
            {
                return new Resultado(true, false,
                    $"/{expressao}/ em {Mostrar(texto)}: grupo {i} deu "
                    + $"{Mostrar(meuGrupo.Valor)} e o .NET deu {Mostrar(deleGrupo.Value)}");
            }
        }

        return new Resultado(true, true, null);
    }

    public static string Mostrar(string texto) =>
        '"' + texto.Replace("\n", "\\n").Replace("\t", "\\t").Replace("\r", "\\r") + '"';

    // -- o gerador ---------------------------------------------------------

    /// <summary>
    /// Uma expressão sorteada, montada pela gramática.
    /// </summary>
    /// <remarks>
    /// Sortear caracteres e torcer para sair uma expressão válida não funciona:
    /// quase tudo é inválido. Montar pela gramática dá expressões sempre
    /// válidas e sempre estranhas — que é exatamente o que se quer de um
    /// gerador, porque as estranhas são as que ninguém escreveria à mão.
    /// </remarks>
    public static string SortearExpressao(Random sorteio, int profundidade = 0)
    {
        var formas = profundidade > 2 ? 3 : 8;

        return sorteio.Next(formas) switch
        {
            0 => "abc"[sorteio.Next(3)].ToString(),
            1 => sorteio.Next(3) switch
            {
                0 => "[ab]",
                1 => "[^a]",
                _ => ".",
            },
            2 => sorteio.Next(4) switch
            {
                0 => "\\d",
                1 => "\\w",
                2 => "\\b",
                _ => "[0-9a-c]",
            },
            3 => SortearExpressao(sorteio, profundidade + 1)
                 + SortearExpressao(sorteio, profundidade + 1),
            4 => SortearExpressao(sorteio, profundidade + 1) + "|"
                 + SortearExpressao(sorteio, profundidade + 1),
            5 => "(" + SortearExpressao(sorteio, profundidade + 1) + ")",
            6 => Envolver(SortearExpressao(sorteio, profundidade + 1), sorteio),
            _ => "(?:" + SortearExpressao(sorteio, profundidade + 1) + ")"
                 + "*?+"[sorteio.Next(3)],
        };
    }

    private static string Envolver(string dentro, Random sorteio)
    {
        var atomo = dentro.Length == 1 ? dentro : "(?:" + dentro + ")";

        return sorteio.Next(6) switch
        {
            0 => atomo + "*",
            1 => atomo + "+",
            2 => atomo + "?",
            3 => atomo + "*?",
            4 => atomo + "{1,2}",
            _ => atomo + "{0,2}",
        };
    }

    public static string SortearTexto(Random sorteio, string alfabeto = "abc019 \n")
    {
        var quantos = sorteio.Next(8);
        var texto = new StringBuilder();

        for (var i = 0; i < quantos; i++)
        {
            texto.Append(alfabeto[sorteio.Next(alfabeto.Length)]);
        }

        return texto.ToString();
    }

    /// <summary>O .NET aceita esta expressão? Se não, não há o que comparar.</summary>
    public static bool ODotnetAceita(string expressao)
    {
        try
        {
            _ = new Dotnet.Regex(expressao);

            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public sealed record Placar(
        int Comparados,
        int CorrespondenciaBate,
        int CapturasBatem,
        List<string> Queixas)
    {
        public double TaxaDeCorrespondencia =>
            Comparados == 0 ? 0 : 100.0 * CorrespondenciaBate / Comparados;

        public double TaxaDeCapturas =>
            Comparados == 0 ? 0 : 100.0 * CapturasBatem / Comparados;
    }

    /// <summary>Roda um lote sorteado e devolve o placar.</summary>
    public static Placar Lote(int semente, int quantas, int textosPorExpressao = 3)
    {
        var sorteio = new Random(semente);

        var comparados = 0;
        var correspondencia = 0;
        var capturas = 0;
        var queixas = new List<string>();

        for (var i = 0; i < quantas; i++)
        {
            var expressao = SortearExpressao(sorteio);

            if (!ODotnetAceita(expressao))
            {
                continue;
            }

            for (var j = 0; j < textosPorExpressao; j++)
            {
                var texto = SortearTexto(sorteio);
                var resultado = Comparar(expressao, texto);

                comparados++;

                if (resultado.CorrespondenciaBate)
                {
                    correspondencia++;
                }

                if (resultado.CapturasBatem)
                {
                    capturas++;
                }
                else if (queixas.Count < 5 && resultado.Queixa is not null)
                {
                    queixas.Add(resultado.Queixa);
                }
            }
        }

        return new Placar(comparados, correspondencia, capturas, queixas);
    }
}

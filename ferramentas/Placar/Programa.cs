using System.Text;
using Conde.Regex;
using Dotnet = System.Text.RegularExpressions;

namespace Conde.Regex.Ferramentas;

/// <summary>
/// O placar: quanto este motor concorda com o <c>System.Text.RegularExpressions</c>.
/// </summary>
/// <remarks>
/// <para>
/// Existe porque um número vago não serve para nada. "Funciona bem" não diz se
/// uma mudança melhorou ou piorou, e num motor de expressões regulares cada
/// mudança mexe em dezenas de casos de canto ao mesmo tempo.
/// </para>
/// <para>
/// Foi ele que decidiu a construção do laço. Há duas formas de fechar um
/// <c>x*</c> — um pulo de volta ou uma segunda divisão —, as duas parecem
/// certas lendo o código, e elas trocam de lado: uma acerta mais
/// correspondências, a outra acerta mais capturas. Sem medir, a escolha seria
/// pelo argumento mais convincente, que é a pior forma de escolher.
/// </para>
/// </remarks>
public static class Programa
{
    public static int Main(string[] argumentos)
    {
        var quantas = argumentos.Length > 0 ? int.Parse(argumentos[0]) : 4_000;

        Console.WriteLine($"{quantas} expressões sorteadas por corpus, três textos cada,");
        Console.WriteLine("contra o System.Text.RegularExpressions rodando de verdade.");
        Console.WriteLine();

        Console.WriteLine("volta do laço".PadRight(20)
            + "comparados".PadLeft(12) + "correspondência".PadLeft(16)
            + "capturas".PadLeft(12));
        Console.WriteLine(new string('-', 62));

        var queixas = new List<string>();
        var vencedora = false;
        var melhorTaxa = -1.0;

        foreach (var comoDivisao in (bool[])[false, true])
        {
            Compilador.VoltaComoDivisao = comoDivisao;

            var total = 0;
            var certos = 0;
            var comCapturas = 0;

            var daqui = new List<string>();

            foreach (var semente in (int[])[1, 2, 3, 20260929])
            {
                var placar = Medir(semente, quantas);

                total += placar.Comparados;
                certos += placar.CorrespondenciaBate;
                comCapturas += placar.CapturasBatem;

                daqui.AddRange(placar.Queixas.Take(2));
            }

            var taxa = 100.0 * certos / total;
            var nome = comoDivisao ? "divisão nova" : "pulo de volta";

            Console.WriteLine(nome.PadRight(20)
                + total.ToString().PadLeft(12)
                + $"{taxa:F3}%".PadLeft(16)
                + $"{100.0 * comCapturas / total:F3}%".PadLeft(12));

            if (taxa > melhorTaxa)
            {
                melhorTaxa = taxa;
                vencedora = comoDivisao;
                queixas = daqui;
            }
        }

        Compilador.VoltaComoDivisao = vencedora;

        Console.WriteLine();
        Console.WriteLine($"a construção escolhida é \"{(vencedora ? "divisão nova" : "pulo de volta")}\".");

        if (queixas.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("as primeiras divergências, para quem quiser olhar:");
            Console.WriteLine();

            foreach (var queixa in queixas.Take(6))
            {
                Console.WriteLine("  " + queixa);
            }
        }

        return melhorTaxa >= 99.9 ? 0 : 1;
    }

    private sealed record Resultado(
        int Comparados, int CorrespondenciaBate, int CapturasBatem, List<string> Queixas)
    {
        public double TaxaDeCorrespondencia =>
            Comparados == 0 ? 0 : 100.0 * CorrespondenciaBate / Comparados;

        public double TaxaDeCapturas =>
            Comparados == 0 ? 0 : 100.0 * CapturasBatem / Comparados;
    }

    private static Resultado Medir(int semente, int quantas)
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

            var meu = new Expressao(expressao);
            var dele = new Dotnet.Regex(expressao);

            for (var j = 0; j < 3; j++)
            {
                var texto = SortearTexto(sorteio);

                comparados++;

                var meuAchado = meu.Procurar(texto);
                var deleAchado = dele.Match(texto);

                var bate = meuAchado.Sucesso == deleAchado.Success
                    && (!deleAchado.Success
                        || (meuAchado.Inicio == deleAchado.Index
                            && meuAchado.Valor == deleAchado.Value));

                if (bate)
                {
                    correspondencia++;
                }
                else if (queixas.Count < 8)
                {
                    queixas.Add(
                        $"/{expressao}/ em \"{texto.Replace("\n", "\\n")}\": "
                        + $"eu dei \"{meuAchado.Valor}\" e o .NET deu \"{deleAchado.Value}\"");
                }

                if (bate && CapturasBatem(meuAchado, deleAchado))
                {
                    capturas++;
                }
                else if (bate && queixas.Count < 8)
                {
                    queixas.Add(
                        $"/{expressao}/ em \"{texto.Replace("\n", "\\n")}\": "
                        + "as capturas divergem");
                }
            }
        }

        return new Resultado(comparados, correspondencia, capturas, queixas);
    }

    private static bool CapturasBatem(Correspondencia meu, Dotnet.Match dele)
    {
        if (!dele.Success)
        {
            return true;
        }

        for (var i = 1; i < dele.Groups.Count; i++)
        {
            if (meu[i].Sucesso != dele.Groups[i].Success)
            {
                return false;
            }

            if (dele.Groups[i].Success && meu[i].Valor != dele.Groups[i].Value)
            {
                return false;
            }
        }

        return true;
    }

    private static bool ODotnetAceita(string expressao)
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

    private static string SortearExpressao(Random sorteio, int profundidade = 0)
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

    private static string SortearTexto(Random sorteio)
    {
        var quantos = sorteio.Next(8);
        var texto = new StringBuilder();

        for (var i = 0; i < quantos; i++)
        {
            texto.Append("abc019 \n"[sorteio.Next(8)]);
        }

        return texto.ToString();
    }
}

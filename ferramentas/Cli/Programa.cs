using Conde.Regex;

namespace Conde.Regex.Ferramentas.Cli;

/// <summary>
/// A linha de comando: um <c>grep</c> pequeno, e um jeito de ver por dentro.
/// </summary>
/// <remarks>
/// <para>
/// O comando <c>ver</c> é o mais interessante dos quatro. Ele imprime o
/// programa compilado, instrução por instrução — e ver <c>a*</c> virar quatro
/// linhas, com o <c>dividir</c> apontando para o corpo e para a saída, é o que
/// torna a construção de Thompson óbvia depois de ter parecido mágica.
/// </para>
/// </remarks>
public static class Programa
{
    public static int Main(string[] argumentos)
    {
        if (argumentos.Length < 2)
        {
            Console.WriteLine(Ajuda);

            return 1;
        }

        var comando = argumentos[0];
        var expressao = argumentos[1];

        try
        {
            return comando switch
            {
                "ver" => Ver(expressao),
                "casar" => Casar(expressao, argumentos),
                "achar" => Achar(expressao, argumentos),
                "trocar" => Trocar(expressao, argumentos),
                _ => Desconhecido(comando),
            };
        }
        catch (ErroDeExpressao erro)
        {
            Console.Error.WriteLine($"/{expressao}/: {erro.Message}");

            // Aponta o caractere, com um acento circunflexo embaixo. É o que
            // um compilador faz, e é muito mais útil que a mensagem sozinha.
            Console.Error.WriteLine($"  {expressao}");
            Console.Error.WriteLine("  " + new string(' ', Math.Max(0, erro.Posicao)) + "^");

            return 2;
        }
        catch (IOException erro)
        {
            Console.Error.WriteLine(erro.Message);

            return 2;
        }
    }

    private static int Desconhecido(string comando)
    {
        Console.Error.WriteLine($"não conheço o comando {comando}");
        Console.WriteLine(Ajuda);

        return 1;
    }

    private static int Ver(string expressao)
    {
        var compilada = new Expressao(expressao);

        Console.WriteLine($"/{expressao}/");
        Console.WriteLine($"{compilada.Programa.Instrucoes.Count} instrução(ões), "
            + $"{compilada.Programa.QuantosGrupos} grupo(s) de captura");
        Console.WriteLine();
        Console.Write(compilada.Programa);

        return 0;
    }

    private static int Casar(string expressao, string[] argumentos)
    {
        if (argumentos.Length < 3)
        {
            Console.Error.WriteLine("falta o texto");

            return 1;
        }

        var compilada = new Expressao(expressao);
        var achado = compilada.Procurar(argumentos[2]);

        if (!achado.Sucesso)
        {
            Console.WriteLine("não casa");

            return 1;
        }

        Console.WriteLine($"casa: \"{achado.Valor}\" em {achado.Inicio}");

        for (var i = 1; i < achado.QuantosGrupos; i++)
        {
            Console.WriteLine($"  grupo {i}: {achado[i]}");
        }

        return 0;
    }

    private static int Achar(string expressao, string[] argumentos)
    {
        var compilada = new Expressao(expressao);

        var linhas = argumentos.Length > 2
            ? File.ReadLines(argumentos[2])
            : LerDaEntrada();

        var quantas = 0;
        var numero = 0;

        foreach (var linha in linhas)
        {
            numero++;

            if (compilada.Casa(linha))
            {
                Console.WriteLine($"{numero}: {linha}");
                quantas++;
            }
        }

        return quantas > 0 ? 0 : 1;
    }

    private static IEnumerable<string> LerDaEntrada()
    {
        while (Console.ReadLine() is { } linha)
        {
            yield return linha;
        }
    }

    private static int Trocar(string expressao, string[] argumentos)
    {
        if (argumentos.Length < 4)
        {
            Console.Error.WriteLine("uso: trocar EXPRESSÃO POR TEXTO");

            return 1;
        }

        Console.WriteLine(new Expressao(expressao).Trocar(argumentos[3], argumentos[2]));

        return 0;
    }

    private const string Ajuda = """
        regex -- um motor de expressões regulares sem retrocesso

          regex ver    EXPRESSÃO            o programa compilado, instrução por instrução
          regex casar  EXPRESSÃO TEXTO      casa, e mostra os grupos
          regex achar  EXPRESSÃO [ARQUIVO]  as linhas que casam, como um grep
          regex trocar EXPRESSÃO POR TEXTO  troca toda correspondência

        Sem ARQUIVO, o `achar` lê a entrada padrão.
        """;
}

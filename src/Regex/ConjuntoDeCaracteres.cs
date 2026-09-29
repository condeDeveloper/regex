using System.Text;

namespace Conde.Regex;

/// <summary>
/// Um conjunto de caracteres, guardado como faixas.
/// </summary>
/// <remarks>
/// <para>
/// A representação óbvia seria um <c>HashSet&lt;char&gt;</c>. Ela funciona e
/// desperdiça: <c>[^x]</c> são 65.535 caracteres, e guardá-los um a um custa
/// meio megabyte por classe numa expressão que pode ter dezenas.
/// </para>
/// <para>
/// Faixas ordenadas e sem sobreposição resolvem: <c>[a-z0-9_]</c> são três
/// pares de números, a negação é uma varredura linear, e o teste de
/// pertinência é uma busca binária. Uma expressão de duzentos caracteres cabe
/// em alguns kilobytes.
/// </para>
/// <para>
/// A normalização — ordenar e fundir as faixas que se tocam — acontece uma vez,
/// na construção. Depois disso a busca binária vale, e ela é o que torna o
/// custo por caractere constante em vez de proporcional ao tamanho da classe.
/// </para>
/// </remarks>
public sealed class ConjuntoDeCaracteres
{
    private readonly (char De, char Ate)[] _faixas;

    public bool Negado { get; }

    private ConjuntoDeCaracteres((char, char)[] faixas, bool negado)
    {
        _faixas = faixas;
        Negado = negado;
    }

    /// <summary>Um caractere só.</summary>
    public static ConjuntoDeCaracteres De(char letra) =>
        new([(letra, letra)], negado: false);

    /// <summary>Uma faixa, como <c>a-z</c>.</summary>
    public static ConjuntoDeCaracteres Faixa(char de, char ate)
    {
        if (de > ate)
        {
            throw new ArgumentException($"a faixa {de}-{ate} está ao contrário");
        }

        return new([(de, ate)], negado: false);
    }

    /// <summary>Todo caractere menos a quebra de linha, que é o que o ponto é.</summary>
    /// <remarks>
    /// O ponto não casar <c>\n</c> é uma herança do <c>grep</c>, que trabalhava
    /// linha a linha e nunca via a quebra. O comportamento sobreviveu a todas as
    /// implementações desde, e mudá-lo aqui daria uma biblioteca que discorda
    /// de todas as outras por uma boa razão — que é a pior espécie de discordância.
    /// </remarks>
    public static ConjuntoDeCaracteres Ponto() =>
        new([('\n', '\n')], negado: true);

    public static ConjuntoDeCaracteres Tudo() =>
        new([], negado: true);

    public static ConjuntoDeCaracteres Nada() =>
        new([], negado: false);

    /// <summary>Junta faixas soltas num conjunto normalizado.</summary>
    public static ConjuntoDeCaracteres DeFaixas(
        IEnumerable<(char De, char Ate)> faixas, bool negado)
    {
        var ordenadas = faixas.OrderBy(f => f.De).ThenBy(f => f.Ate).ToList();
        var juntas = new List<(char De, char Ate)>();

        foreach (var faixa in ordenadas)
        {
            if (juntas.Count > 0)
            {
                var ultima = juntas[^1];

                // O `+ 1` é o que funde `[a-c][d-f]` em `[a-f]`. Sem ele as duas
                // faixas ficam vizinhas e separadas, o que dá o mesmo resultado
                // e uma busca binária mais funda.
                if (faixa.De <= ultima.Ate || faixa.De == ultima.Ate + 1)
                {
                    juntas[^1] = (ultima.De, (char)Math.Max(ultima.Ate, faixa.Ate));
                    continue;
                }
            }

            juntas.Add(faixa);
        }

        return new ConjuntoDeCaracteres([.. juntas], negado);
    }

    /// <summary>O caractere está no conjunto?</summary>
    public bool Contem(char letra)
    {
        var dentro = BuscaBinaria(letra);

        return Negado ? !dentro : dentro;
    }

    private bool BuscaBinaria(char letra)
    {
        var baixo = 0;
        var alto = _faixas.Length - 1;

        while (baixo <= alto)
        {
            var meio = (baixo + alto) / 2;
            var faixa = _faixas[meio];

            if (letra < faixa.De)
            {
                alto = meio - 1;
            }
            else if (letra > faixa.Ate)
            {
                baixo = meio + 1;
            }
            else
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>As classes com nome, que são açúcar para faixas.</summary>
    public static ConjuntoDeCaracteres? PelaLetra(char letra) => letra switch
    {
        'd' => DeFaixas([('0', '9')], negado: false),
        'D' => DeFaixas([('0', '9')], negado: true),
        'w' => Palavra(negado: false),
        'W' => Palavra(negado: true),
        's' => Espaco(negado: false),
        'S' => Espaco(negado: true),
        _ => null,
    };

    /// <summary>
    /// O <c>\w</c>: letra, dígito e o sublinhado.
    /// </summary>
    /// <remarks>
    /// O sublinhado estar aí e o hífen não é uma decisão de 1986 do Perl, e ela
    /// vem de o <c>\w</c> ter nascido para casar identificadores de C — onde o
    /// sublinhado faz parte do nome e o hífen é subtração.
    /// </remarks>
    private static ConjuntoDeCaracteres Palavra(bool negado) =>
        DeFaixas([('a', 'z'), ('A', 'Z'), ('0', '9'), ('_', '_')], negado);

    private static ConjuntoDeCaracteres Espaco(bool negado) =>
        DeFaixas(
            [(' ', ' '), ('\t', '\t'), ('\n', '\n'), ('\r', '\r'),
             ('\f', '\f'), ('\v', '\v')],
            negado);

    /// <summary>Um caractere é de palavra? Usado pela fronteira <c>\b</c>.</summary>
    public static bool EhDePalavra(char letra) =>
        char.IsAsciiLetterOrDigit(letra) || letra == '_';

    public override string ToString()
    {
        var texto = new StringBuilder("[");

        if (Negado)
        {
            texto.Append('^');
        }

        foreach (var (de, ate) in _faixas)
        {
            texto.Append(Mostrar(de));

            if (ate != de)
            {
                texto.Append('-').Append(Mostrar(ate));
            }
        }

        return texto.Append(']').ToString();
    }

    private static string Mostrar(char letra) => letra switch
    {
        '\n' => "\\n",
        '\r' => "\\r",
        '\t' => "\\t",
        _ when char.IsControl(letra) => $"\\x{(int)letra:x2}",
        _ => letra.ToString(),
    };
}

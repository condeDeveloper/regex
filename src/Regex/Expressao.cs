using System.Text;

namespace Conde.Regex;

/// <summary>Uma captura: onde ela começou e o que ela pegou.</summary>
public readonly record struct Captura(int Inicio, int Comprimento, string Valor)
{
    /// <summary>
    /// Um grupo que não participou da correspondência.
    /// </summary>
    /// <remarks>
    /// Em <c>(a)|(b)</c> contra "b", o grupo 1 não casou. Devolver texto vazio
    /// seria mentir: "casou o vazio" e "não casou" são estados diferentes, e a
    /// diferença aparece em qualquer código que pergunte <c>Sucesso</c>.
    /// </remarks>
    public static readonly Captura Nenhuma = new(-1, 0, string.Empty);

    public bool Sucesso => Inicio >= 0;

    public override string ToString() => Sucesso ? $"\"{Valor}\"@{Inicio}" : "(não casou)";
}

/// <summary>O resultado de uma busca.</summary>
public sealed class Correspondencia
{
    private readonly Captura[] _grupos;

    internal Correspondencia(Captura[] grupos)
    {
        _grupos = grupos;
    }

    public static readonly Correspondencia Nenhuma = new([]);

    public bool Sucesso => _grupos.Length > 0;

    public int Inicio => Sucesso ? _grupos[0].Inicio : -1;

    public int Comprimento => Sucesso ? _grupos[0].Comprimento : 0;

    public string Valor => Sucesso ? _grupos[0].Valor : string.Empty;

    public int QuantosGrupos => _grupos.Length;

    /// <summary>O grupo 0 é a correspondência inteira.</summary>
    public Captura this[int numero] =>
        numero >= 0 && numero < _grupos.Length ? _grupos[numero] : Captura.Nenhuma;

    public override string ToString() => Sucesso
        ? $"\"{Valor}\" em {Inicio}..{Inicio + Comprimento}"
        : "(sem correspondência)";
}

/// <summary>
/// A fachada: compila uma expressão e a usa.
/// </summary>
/// <remarks>
/// <para>
/// A interface é deliberadamente parecida com a do <c>System.Text.RegularExpressions</c>,
/// porque é contra ela que este código é conferido. Nomes diferentes fariam a
/// comparação nos testes ficar cheia de tradução, e tradução no teste é onde
/// um engano se esconde.
/// </para>
/// </remarks>
public sealed class Expressao
{
    private readonly Maquina _maquina;

    public string Texto { get; }

    public Programa Programa { get; }

    public Expressao(string expressao)
    {
        Texto = expressao;

        var (arvore, quantosGrupos) = Analisador.Analisar(expressao);

        Programa = Compilador.Compilar(arvore, quantosGrupos);
        _maquina = new Maquina(Programa);
    }

    public bool Casa(string texto) => Procurar(texto).Sucesso;

    public Correspondencia Procurar(string texto) => Procurar(texto, 0);

    public Correspondencia Procurar(string texto, int de)
    {
        if (de < 0 || de > texto.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(de));
        }

        var compartimentos = _maquina.Procurar(texto, de);

        if (compartimentos is null)
        {
            return Correspondencia.Nenhuma;
        }

        var grupos = new Captura[Programa.QuantosGrupos + 1];

        for (var i = 0; i <= Programa.QuantosGrupos; i++)
        {
            var inicio = compartimentos[i * 2];
            var fim = compartimentos[i * 2 + 1];

            grupos[i] = inicio >= 0 && fim >= inicio
                ? new Captura(inicio, fim - inicio, texto[inicio..fim])
                : Captura.Nenhuma;
        }

        return new Correspondencia(grupos);
    }

    /// <summary>Todas as correspondências, sem sobreposição.</summary>
    /// <remarks>
    /// <para>
    /// O cuidado está na correspondência vazia. <c>a*</c> casa o vazio em
    /// qualquer posição, e avançar "até o fim da correspondência" deixaria o
    /// laço parado para sempre. A saída, que é a de todas as implementações, é
    /// avançar um caractere à força quando a correspondência não consumiu nada.
    /// </para>
    /// </remarks>
    public IEnumerable<Correspondencia> Todas(string texto)
    {
        var posicao = 0;

        while (posicao <= texto.Length)
        {
            var achada = Procurar(texto, posicao);

            if (!achada.Sucesso)
            {
                yield break;
            }

            yield return achada;

            posicao = achada.Comprimento == 0
                ? achada.Inicio + 1
                : achada.Inicio + achada.Comprimento;
        }
    }

    /// <summary>Troca toda correspondência por outro texto.</summary>
    /// <remarks>
    /// O <c>$1</c> na troca vira o grupo 1, e o <c>$$</c> vira um cifrão. É a
    /// mesma convenção do .NET, pelo mesmo motivo de sempre: ser diferente do
    /// juiz por gosto próprio só cria armadilha para quem usa os dois.
    /// </remarks>
    public string Trocar(string texto, string por)
    {
        var saida = new StringBuilder();
        var posicao = 0;

        foreach (var achada in Todas(texto))
        {
            saida.Append(texto, posicao, achada.Inicio - posicao);
            saida.Append(Expandir(por, achada));

            posicao = achada.Inicio + achada.Comprimento;
        }

        saida.Append(texto, posicao, texto.Length - posicao);

        return saida.ToString();
    }

    private static string Expandir(string modelo, Correspondencia achada)
    {
        var saida = new StringBuilder();

        for (var i = 0; i < modelo.Length; i++)
        {
            if (modelo[i] != '$' || i + 1 >= modelo.Length)
            {
                saida.Append(modelo[i]);
                continue;
            }

            if (modelo[i + 1] == '$')
            {
                saida.Append('$');
                i++;
                continue;
            }

            var comeco = i + 1;
            var fim = comeco;

            while (fim < modelo.Length && char.IsAsciiDigit(modelo[fim]))
            {
                fim++;
            }

            if (fim == comeco)
            {
                saida.Append('$');
                continue;
            }

            var numero = int.Parse(modelo[comeco..fim]);

            saida.Append(achada[numero].Valor);

            i = fim - 1;
        }

        return saida.ToString();
    }

    /// <summary>Parte o texto nas correspondências.</summary>
    public IReadOnlyList<string> Partir(string texto)
    {
        var pedacos = new List<string>();
        var posicao = 0;

        foreach (var achada in Todas(texto))
        {
            if (achada.Comprimento == 0 && achada.Inicio == posicao)
            {
                continue;
            }

            pedacos.Add(texto[posicao..achada.Inicio]);

            posicao = achada.Inicio + achada.Comprimento;
        }

        pedacos.Add(texto[posicao..]);

        return pedacos;
    }

    public override string ToString() => $"/{Texto}/";
}

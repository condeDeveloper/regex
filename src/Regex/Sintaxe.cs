namespace Conde.Regex;

/// <summary>
/// A árvore de uma expressão regular, e o erro de quem a escreveu errado.
/// </summary>
/// <remarks>
/// <para>
/// A árvore tem seis formas, e é notável que seis bastem. Uma expressão regular
/// de verdade — com classes, quantificadores, grupos, alternativas e âncoras —
/// se reduz a estas seis, e todo o resto é açúcar que o analisador desfaz antes
/// de chegar aqui.
/// </para>
/// <para>
/// O <c>a{2,4}</c> vira <c>aa(a(a)?)?</c>. O <c>\d</c> vira uma classe. O
/// <c>+</c> vira concatenação com repetição. Desfazer o açúcar cedo é o que
/// mantém o compilador e a máquina pequenos — e pequeno, aqui, quer dizer
/// auditável: o laço que executa a expressão tem quarenta linhas.
/// </para>
/// </remarks>
public abstract record No
{
    /// <summary>Um caractere, ou um conjunto deles.</summary>
    public sealed record Classe(ConjuntoDeCaracteres Conjunto) : No;

    /// <summary>Um atrás do outro: <c>ab</c>.</summary>
    public sealed record Sequencia(IReadOnlyList<No> Partes) : No;

    /// <summary>Um ou outro: <c>a|b</c>.</summary>
    public sealed record Alternativa(No Esquerda, No Direita) : No;

    /// <summary>
    /// Zero ou mais, com a ganância explícita.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A ganância não é detalhe de conforto: ela decide <b>qual</b> das
    /// respostas certas sai. Em <c>&lt;.*&gt;</c> sobre
    /// <c>&lt;a&gt;&lt;b&gt;</c>, o guloso casa a linha inteira e o preguiçoso
    /// casa só <c>&lt;a&gt;</c> — as duas são correspondências válidas, e a
    /// diferença entre elas já quebrou muito raspador de HTML.
    /// </para>
    /// </remarks>
    public sealed record Repeticao(No Dentro, bool Guloso) : No;

    /// <summary>Zero ou um: <c>a?</c>.</summary>
    public sealed record Opcional(No Dentro, bool Guloso) : No;

    /// <summary>Um grupo que captura, ou um que só agrupa.</summary>
    public sealed record Grupo(No Dentro, int Numero) : No;

    /// <summary>O começo ou o fim do texto.</summary>
    public sealed record Ancora(TipoDeAncora Tipo) : No;

    /// <summary>O vazio, que casa sem consumir nada. Sai de <c>(|a)</c>.</summary>
    public sealed record Vazio : No;
}

public enum TipoDeAncora
{
    Comeco,
    Fim,

    /// <summary>A fronteira de palavra, <c>\b</c>.</summary>
    Fronteira,

    /// <summary>O contrário dela, <c>\B</c>.</summary>
    ForaDeFronteira,
}

/// <summary>Um erro na expressão, com a posição do caractere que o causou.</summary>
/// <remarks>
/// A posição é o que separa uma mensagem útil de um "expressão inválida". Numa
/// expressão de oitenta caracteres com três grupos aninhados, saber que o
/// problema está no caractere 47 é a diferença entre corrigir em dez segundos e
/// olhar a linha inteira procurando.
/// </remarks>
public sealed class ErroDeExpressao(string mensagem, int posicao)
    : Exception($"posição {posicao}: {mensagem}")
{
    public int Posicao { get; } = posicao;
}

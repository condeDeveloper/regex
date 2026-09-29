using Xunit;
using Dotnet = System.Text.RegularExpressions;

namespace Conde.Regex.Testes;

/// <summary>
/// O analisador: a árvore certa, e a recusa certa.
/// </summary>
/// <remarks>
/// Recusar é metade do trabalho de um analisador, e é a metade que ninguém
/// testa. Uma expressão malformada tem de dar erro <b>com a posição</b>, e não
/// uma árvore torta que só vai dar problema na hora de casar.
/// </remarks>
public class AnalisadorTest
{
    [Theory(DisplayName = "o que o .NET recusa, eu recuso também")]
    [InlineData("(")]
    [InlineData(")")]
    [InlineData("[")]
    [InlineData("[a")]
    [InlineData("a)")]
    [InlineData("(a")]
    [InlineData("*")]
    [InlineData("+")]
    [InlineData("?")]
    [InlineData("a**+")]
    [InlineData("[z-a]")]
    [InlineData("\\")]
    public void OQueNaoEntraDeJeitoNenhum(string expressao)
    {
        // O .NET é o árbitro do que é inválido. Recusar o que ele aceita seria
        // tão errado quanto aceitar o que ele recusa -- e a segunda metade é a
        // que costuma passar despercebida.
        var oDotnetRecusa = false;

        try
        {
            _ = new Dotnet.Regex(expressao);
        }
        catch (ArgumentException)
        {
            oDotnetRecusa = true;
        }

        if (!oDotnetRecusa)
        {
            // O .NET aceita `a**+`, por exemplo. Se ele aceita, eu tenho de
            // aceitar, e o teste vira o contrário.
            _ = new Expressao(expressao);

            return;
        }

        Assert.ThrowsAny<Exception>(() => new Expressao(expressao));
    }

    [Fact(DisplayName = "o erro diz em que caractere")]
    public void OErroDizOnde()
    {
        var erro = Assert.Throws<ErroDeExpressao>(() => new Expressao("abc(de"));

        Assert.True(erro.Posicao >= 4,
            $"a posição devia apontar para o parêntese aberto, e veio {erro.Posicao}");

        Assert.Contains("posição", erro.Message);

        var outro = Assert.Throws<ErroDeExpressao>(() => new Expressao("ab[cd"));

        Assert.Contains("colchete", outro.Message);
    }

    [Fact(DisplayName = "a precedência sai da ordem das funções, e não de uma tabela")]
    public void Precedencia()
    {
        // `ab|cd` são duas sequências inteiras competindo, e não `b` com `c`.
        var alternativa = new Expressao("ab|cd");

        Assert.True(alternativa.Casa("ab"));
        Assert.True(alternativa.Casa("cd"));
        Assert.False(alternativa.Casa("acd") && alternativa.Procurar("acd").Valor == "acd");

        // `ab*` prende o asterisco só no `b`.
        var repeticao = new Expressao("^ab*$");

        Assert.True(repeticao.Casa("a"));
        Assert.True(repeticao.Casa("abbb"));
        Assert.False(repeticao.Casa("ababab"));
    }

    [Fact(DisplayName = "o {n,m} vira cópias, e o programa cresce com o número")]
    public void ContagemViraCopias()
    {
        var pequena = new Expressao("a{2}");
        var grande = new Expressao("a{200}");

        Assert.True(
            grande.Programa.Instrucoes.Count > pequena.Programa.Instrucoes.Count * 20,
            "o programa devia crescer com a contagem");

        // E o resultado é o mesmo que o da forma escrita à mão.
        foreach (var texto in (string[])["a", "aa", "aaa", ""])
        {
            Assert.Equal(new Expressao("^aa$").Casa(texto), new Expressao("^a{2}$").Casa(texto));
        }
    }

    [Fact(DisplayName = "o grupo que não captura não gasta compartimento")]
    public void GrupoQueNaoCaptura()
    {
        var comCaptura = new Expressao("(ab)+");
        var sem = new Expressao("(?:ab)+");

        Assert.Equal(1, comCaptura.Programa.QuantosGrupos);
        Assert.Equal(0, sem.Programa.QuantosGrupos);

        // E os dois casam a mesma coisa.
        Assert.Equal(comCaptura.Procurar("ababab").Valor, sem.Procurar("ababab").Valor);
    }

    [Fact(DisplayName = "as classes com nome são açúcar para faixas")]
    public void ClassesComNome()
    {
        (string Nome, string Equivalente)[] pares =
        [
            ("\\d", "[0-9]"),
            ("\\D", "[^0-9]"),
            ("\\w", "[a-zA-Z0-9_]"),
            ("\\W", "[^a-zA-Z0-9_]"),
        ];

        var textos = new[] { "a", "1", "_", " ", "!", "Z", "" };

        foreach (var (nome, equivalente) in pares)
        {
            foreach (var texto in textos)
            {
                Assert.True(
                    new Expressao(nome).Casa(texto) == new Expressao(equivalente).Casa(texto),
                    $"{nome} e {equivalente} discordam em \"{texto}\"");
            }
        }
    }

    [Fact(DisplayName = "o programa compilado é legível")]
    public void OProgramaEhLegivel()
    {
        // Um formato binário -- ou um programa compilado -- só fica
        // compreensível quando dá para ver o que ele virou. É a mesma razão do
        // comando `ver` da linha de comando.
        var texto = new Expressao("a|b").Programa.ToString();

        Assert.Contains("dividir", texto);
        Assert.Contains("casar", texto);
        Assert.Contains("parar", texto);
    }
}

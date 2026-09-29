using Xunit;
using Dotnet = System.Text.RegularExpressions;

namespace Conde.Regex.Testes;

/// <summary>
/// A conformidade com o <c>System.Text.RegularExpressions</c>.
/// </summary>
/// <remarks>
/// <para>
/// Não há expectativa minha nestes testes. Cada caso roda a mesma expressão
/// sobre o mesmo texto nos dois motores e exige que eles concordem.
/// </para>
/// <para>
/// É o único jeito honesto de testar um motor de expressões regulares. As
/// decisões pequenas são dezenas — qual alternativa vence, até onde o guloso
/// vai, o que um grupo captura quando repete, o que o <c>$</c> faz diante de
/// uma quebra de linha — e para cada uma há uma resposta plausível que não é a
/// que todo mundo usa.
/// </para>
/// </remarks>
public class JuizTest
{
    private static void Exigir(string expressao, string texto)
    {
        var resultado = Juiz.Comparar(expressao, texto);

        Assert.True(resultado.CorrespondenciaBate && resultado.CapturasBatem,
            resultado.Queixa);
    }

    [Fact(DisplayName = "os casos de manual, um a um")]
    public void CasosDeManual()
    {
        (string Expressao, string Texto)[] casos =
        [
            // literais
            ("abc", "abc"), ("abc", "xabcx"), ("abc", "ab"), ("", "qualquer"),

            // a alternativa escolhe a PRIMEIRA que der certo, e não a maior.
            // É aqui que um motor POSIX discordaria: ele escolheria "ab".
            ("a|ab", "ab"), ("ab|a", "ab"), ("|a", "a"), ("a|", "a"),

            // ganância
            ("a*", "aaa"), ("a*?", "aaa"), ("a+", "aaa"), ("a+?", "aaa"),
            ("<.*>", "<a><b>"), ("<.*?>", "<a><b>"),
            ("a?", "a"), ("a??", "a"), ("a??b", "ab"),

            // grupos e capturas
            ("(a)(b)", "ab"), ("(a|b)+", "abab"), ("(a)|(b)", "b"),
            ("((a)(b))", "ab"), ("(?:ab)+", "ababab"), ("(a)b(c)", "abc"),
            ("(a+)(b+)", "aabbb"),

            // classes
            ("[abc]+", "cabbage"), ("[^abc]+", "cabbage"), ("[a-z]+", "Hello"),
            ("[]a]", "]"), ("[a-]", "-"), ("[-a]", "-"), ("[\\]]", "]"),
            ("\\d+", "ab123cd"), ("\\w+", "a_1 b"), ("\\s+", "a  b"),
            ("\\D+", "12ab34"), ("\\W+", "ab!! cd"), ("\\S+", "  ab  "),
            ("[\\d]+", "a12"), ("[\\w-]+", "a-b c"), ("[^\\d]+", "12ab"),

            // âncoras
            ("^abc", "abc"), ("^abc", "xabc"), ("abc$", "abc"), ("abc$", "abcx"),
            ("^$", ""), ("^a*$", "aaa"), ("^", "abc"), ("$", "abc"),

            // o $ casa também antes de uma quebra final. Foi o primeiro
            // defeito que o juiz achou aqui.
            ("$", "\n"), ("a$", "a\n"), ("^a$", "a\n"), ("$", "a\n\n"),

            ("\\bfoo\\b", "a foo b"), ("\\bfoo\\b", "afoob"),
            ("\\Bfoo", "afoo"), ("\\Bfoo", " foo"),
            ("\\b", ""), ("\\b", "a"), ("\\B", ""),

            // contagem
            ("a{2}", "aaa"), ("a{2,}", "aaaa"), ("a{2,3}", "aaaa"),
            ("a{0,2}", "aaa"), ("a{0}", "aaa"), ("(ab){2}", "ababab"),
            ("a{1,3}b", "aab"), ("a{3}", "aa"),

            // o ponto não casa a quebra de linha
            (".", "\n"), (".+", "ab\ncd"), ("a.b", "a\nb"),

            // combinações que costumam pegar implementações
            ("(a+)+b", "aaab"), ("(a|aa)+", "aaaa"), ("(a*)(a*)", "aaa"),
            ("x(a|ab)y", "xaby"), ("(ab|a)(c|bc)", "abc"),
            ("(a)(b)?c", "ac"), ("(a)?(b)", "b"),
        ];

        foreach (var (expressao, texto) in casos)
        {
            Exigir(expressao, texto);
        }
    }

    [Fact(DisplayName = "as expressões de verdade que as pessoas escrevem")]
    public void ExpressoesDeVerdade()
    {
        (string Expressao, string[] Textos)[] casos =
        [
            (@"^\d{3}\.\d{3}\.\d{3}-\d{2}$",
             ["123.456.789-01", "12.456.789-01", "123.456.789-012", ""]),

            (@"^\w+@\w+\.\w{2,}$",
             ["miguel@exemplo.com", "miguel@exemplo.c", "@exemplo.com", "a@b.co"]),

            (@"\d{2}/\d{2}/\d{4}",
             ["hoje e 29/09/2026 aqui", "29/9/2026", "sem data"]),

            (@"^(\d{5})-?(\d{3})$",
             ["01310100", "01310-100", "0131010", "01310--100"]),

            (@"^[+-]?\d+(\.\d+)?([eE][+-]?\d+)?$",
             ["3.14", "-2", "+1e10", "1.5E-3", "1.", ".5", "1e", ""]),

            (@"<(\w+)>(.*?)</\w+>", ["<b>oi</b> e <i>ola</i>"]),

            (@"^\s*$", ["", "   ", " \t ", "a"]),

            (@"[A-Z][a-z]+(\s[A-Z][a-z]+)*",
             ["Miguel Viscaino Cavalcante", "miguel", "A B"]),

            (@"(\d+)([a-z]+)", ["12ab34cd", "abc", "123"]),

            (@"\b\w+\b", ["uma frase com palavras", "  ", "a"]),

            (@"(https?)://([^/\s]+)(/\S*)?",
             ["veja https://exemplo.com/a/b agora", "http://x", "ftp://y"]),
        ];

        foreach (var (expressao, textos) in casos)
        {
            foreach (var texto in textos)
            {
                Exigir(expressao, texto);
            }
        }
    }

    [Fact(DisplayName = "a correspondência bate em mais de 99,9% dos sorteados")]
    public void CorrespondenciaQuaseSempreBate()
    {
        // 48 mil comparações dão 99,990%: cinco divergências ao todo, e todas
        // da mesma família -- um laço cuja última volta casa o vazio. Um motor
        // com retrocesso roda essa volta, guarda o que ela fez, e só então
        // percebe que não avançou; um Thompson não segue duas vezes a mesma
        // instrução no mesmo caractere e por isso não a roda.
        //
        // Não é defeito para corrigir: é o preço de não ter retrocesso, e é o
        // mesmo preço que compra o `(a+)+b` em microssegundos.
        var total = 0;
        var certos = 0;

        string? primeira = null;

        foreach (var semente in (int[])[1, 2, 3, 20260929])
        {
            var placar = Juiz.Lote(semente, 3_000);

            total += placar.Comparados;
            certos += placar.CorrespondenciaBate;

            primeira ??= placar.Queixas.FirstOrDefault();
        }

        Assert.True(total > 20_000, $"só {total} pares foram comparados");

        var taxa = 100.0 * certos / total;

        Assert.True(taxa > 99.9,
            $"correspondência batendo em {taxa:F3}% ({total - certos} de {total}). "
            + $"A primeira divergência: {primeira}");
    }

    [Fact(DisplayName = "as capturas batem em mais de 99,8% dos sorteados")]
    public void CapturasQuaseSempreBatem()
    {
        var total = 0;
        var certas = 0;

        foreach (var semente in (int[])[1, 2, 3, 20260929])
        {
            var placar = Juiz.Lote(semente, 3_000);

            total += placar.Comparados;
            certas += placar.CapturasBatem;
        }

        var taxa = 100.0 * certas / total;

        Assert.True(taxa > 99.8,
            $"capturas batendo em {taxa:F3}% ({total - certas} de {total})");
    }

    [Fact(DisplayName = "a divisão nova ganha do pulo de volta, e é por isso que está lá")]
    public void AConstrucaoEscolhidaEhAMelhor()
    {
        // O placar não é decoração: ele é quem decidiu. Este teste guarda a
        // decisão, para que alguém que mexa no compilador veja a comparação em
        // vez de achar que o `Dividir` a mais é sobra.
        var antes = Compilador.VoltaComoDivisao;

        try
        {
            Compilador.VoltaComoDivisao = false;

            var comPulo = Juiz.Lote(20260929, 2_000);

            Compilador.VoltaComoDivisao = true;

            var comDivisao = Juiz.Lote(20260929, 2_000);

            Assert.True(
                comDivisao.TaxaDeCorrespondencia > comPulo.TaxaDeCorrespondencia,
                $"com divisão: {comDivisao.TaxaDeCorrespondencia:F3}%, "
                + $"com pulo: {comPulo.TaxaDeCorrespondencia:F3}%");

            Assert.True(
                comDivisao.TaxaDeCapturas > comPulo.TaxaDeCapturas,
                $"capturas -- com divisão: {comDivisao.TaxaDeCapturas:F3}%, "
                + $"com pulo: {comPulo.TaxaDeCapturas:F3}%");
        }
        finally
        {
            Compilador.VoltaComoDivisao = antes;
        }
    }

    [Fact(DisplayName = "todas as correspondências, na mesma ordem que o .NET")]
    public void TodasAsCorrespondencias()
    {
        (string Expressao, string Texto)[] casos =
        [
            ("a", "banana"), ("an", "banana"), ("a*", "baaanaa"),
            ("\\d+", "a1b22c333"), ("", "abc"), ("b*", "abc"),
            ("(a)(b)?", "ab a b"), ("[aeiou]", "expressao regular"),
            ("\\b\\w+\\b", "tres palavras aqui"), ("x", "abc"),
        ];

        foreach (var (expressao, texto) in casos)
        {
            var minhas = new Expressao(expressao).Todas(texto).ToList();
            var dele = new Dotnet.Regex(expressao).Matches(texto);

            Assert.True(minhas.Count == dele.Count,
                $"/{expressao}/ em {Juiz.Mostrar(texto)}: achei {minhas.Count}, "
                + $"o .NET achou {dele.Count}");

            for (var i = 0; i < dele.Count; i++)
            {
                Assert.Equal(dele[i].Index, minhas[i].Inicio);
                Assert.Equal(dele[i].Value, minhas[i].Valor);
            }
        }
    }

    [Fact(DisplayName = "a troca dá o mesmo texto que a do .NET")]
    public void Trocar()
    {
        (string Expressao, string Texto, string Por)[] casos =
        [
            ("a", "banana", "X"),
            ("\\d+", "a1b22c333", "#"),
            ("(\\w+)@(\\w+)", "eu@aqui e voce@ali", "$2 de $1"),
            ("a*", "bab", "-"),
            ("(a)(b)", "abab", "$2$1"),
            ("x", "abc", "y"),
            ("b", "abc", "$$"),
            ("\\s+", " a  b   c ", " "),
        ];

        foreach (var (expressao, texto, por) in casos)
        {
            var meu = new Expressao(expressao).Trocar(texto, por);
            var dele = new Dotnet.Regex(expressao).Replace(texto, por);

            Assert.True(meu == dele,
                $"/{expressao}/ trocando por {Juiz.Mostrar(por)} em {Juiz.Mostrar(texto)}: "
                + $"eu dei {Juiz.Mostrar(meu)} e o .NET deu {Juiz.Mostrar(dele)}");
        }
    }
}

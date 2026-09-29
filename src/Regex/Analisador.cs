namespace Conde.Regex;

/// <summary>
/// Lê o texto da expressão e monta a árvore.
/// </summary>
/// <remarks>
/// <para>
/// É uma descida recursiva sobre uma gramática de quatro níveis, e os níveis
/// <b>são</b> a precedência dos operadores:
/// </para>
/// <code>
///   alternativa := sequencia ('|' sequencia)*
///   sequencia   := repeticao*
///   repeticao   := atomo ('*' | '+' | '?' | '{n,m}')*
///   atomo       := literal | classe | '(' alternativa ')' | ancora
/// </code>
/// <para>
/// A alternativa fica no topo porque é o operador mais fraco: em <c>ab|cd</c>
/// as duas sequências inteiras é que competem, e não <c>b</c> com <c>c</c>. O
/// quantificador fica embaixo porque é o mais forte: em <c>ab*</c> o asterisco
/// prende só no <c>b</c>. Essa é a diferença entre uma expressão que funciona e
/// uma que casa outra coisa — e ela sai da ordem das funções, não de uma tabela.
/// </para>
/// </remarks>
public sealed class Analisador
{
    private readonly string _texto;
    private int _posicao;
    private int _grupos;

    private Analisador(string texto)
    {
        _texto = texto;
    }

    public static (No Arvore, int QuantosGrupos) Analisar(string expressao)
    {
        var analisador = new Analisador(expressao);
        var arvore = analisador.Alternativa();

        if (!analisador.Acabou)
        {
            throw new ErroDeExpressao(
                analisador.Atual == ')'
                    ? "parêntese fechado sem um aberto correspondente"
                    : $"não entendi o caractere '{analisador.Atual}'",
                analisador._posicao);
        }

        return (arvore, analisador._grupos);
    }

    private bool Acabou => _posicao >= _texto.Length;

    private char Atual => _texto[_posicao];

    private bool Olhar(char letra) => !Acabou && Atual == letra;

    private bool Comer(char letra)
    {
        if (!Olhar(letra))
        {
            return false;
        }

        _posicao++;

        return true;
    }

    private char Pegar()
    {
        if (Acabou)
        {
            throw new ErroDeExpressao("a expressão acabou no meio", _posicao);
        }

        return _texto[_posicao++];
    }

    // -- os quatro níveis --------------------------------------------------

    private No Alternativa()
    {
        var esquerda = Sequencia();

        while (Comer('|'))
        {
            esquerda = new No.Alternativa(esquerda, Sequencia());
        }

        return esquerda;
    }

    private No Sequencia()
    {
        var partes = new List<No>();

        while (!Acabou && Atual != '|' && Atual != ')')
        {
            partes.Add(Repeticao());
        }

        return partes.Count switch
        {
            0 => new No.Vazio(),
            1 => partes[0],
            _ => new No.Sequencia(partes),
        };
    }

    /// <summary>
    /// Um átomo, com <b>no máximo um</b> quantificador.
    /// </summary>
    /// <remarks>
    /// <para>
    /// "No máximo um" é a regra, e ela vale para o <c>*</c>, o <c>+</c>, o
    /// <c>?</c> e o <c>{n,m}</c> igualmente: <c>a**</c>, <c>a+*</c> e
    /// <c>a{2}{3}</c> são erro. O que não é erro é o <c>?</c> logo depois de um
    /// quantificador, porque ali ele não é quantificador — é o marcador de
    /// preguiça, e por isso é lido dentro de cada ramo e não fora.
    /// </para>
    /// <para>
    /// A primeira versão aceitava tudo, com um laço no lugar do <c>if</c>, e um
    /// comentário explicando que o .NET aceitava também. O comentário estava
    /// errado — o .NET recusa <c>a**+</c> com "quantificador aninhado" — e foi
    /// o juiz quem apontou. Aceitar o que o juiz recusa é tão errado quanto
    /// recusar o que ele aceita, e é a metade que costuma passar despercebida.
    /// </para>
    /// </remarks>
    private No Repeticao()
    {
        var dentro = Atomo();
        var onde = _posicao;

        if (Comer('*'))
        {
            dentro = new No.Repeticao(dentro, Guloso: !Comer('?'));
        }
        else if (Comer('+'))
        {
            // `a+` é `a` seguido de `a*`. Desfazer o açúcar aqui mantém a
            // máquina com uma instrução a menos.
            var guloso = !Comer('?');

            dentro = new No.Sequencia([dentro, new No.Repeticao(dentro, guloso)]);
        }
        else if (Comer('?'))
        {
            dentro = new No.Opcional(dentro, Guloso: !Comer('?'));
        }
        else if (Olhar('{') && TentarContagem(out var de, out var ate))
        {
            dentro = Contar(dentro, de, ate, onde);
        }
        else
        {
            return dentro;
        }

        ExigirQueNaoVenhaOutro();

        return dentro;
    }

    private void ExigirQueNaoVenhaOutro()
    {
        if (Acabou)
        {
            return;
        }

        if (Atual is '*' or '+')
        {
            throw new ErroDeExpressao(
                $"quantificador aninhado: '{Atual}' logo depois de outro", _posicao);
        }

        // Um `{` só é quantificador se vier um número atrás; `a*{x}` tem uma
        // chave literal e é legítimo.
        if (Olhar('{'))
        {
            var guardado = _posicao;

            if (TentarContagem(out _, out _))
            {
                _posicao = guardado;

                throw new ErroDeExpressao(
                    "quantificador aninhado: '{' logo depois de outro", _posicao);
            }

            _posicao = guardado;
        }
    }

    private No Atomo()
    {
        var onde = _posicao;
        var letra = Pegar();

        switch (letra)
        {
            case '(':
                return Parenteses();

            case '[':
                return new No.Classe(LerClasse());

            case '.':
                return new No.Classe(ConjuntoDeCaracteres.Ponto());

            case '^':
                return new No.Ancora(TipoDeAncora.Comeco);

            case '$':
                return new No.Ancora(TipoDeAncora.Fim);

            case '\\':
                return Escape();

            case '*':
            case '+':
            case '?':
                throw new ErroDeExpressao(
                    $"o quantificador '{letra}' não tem nada antes dele", onde);

            default:
                return new No.Classe(ConjuntoDeCaracteres.De(letra));
        }
    }

    private No Parenteses()
    {
        var capturando = true;

        // `(?:...)` agrupa sem capturar. É o que se usa quando o grupo existe
        // só para o quantificador prender nele.
        if (Olhar('?'))
        {
            _posicao++;

            if (!Comer(':'))
            {
                throw new ErroDeExpressao(
                    "só entendo (?:...) entre os grupos com '?'", _posicao);
            }

            capturando = false;
        }

        var numero = capturando ? ++_grupos : -1;
        var dentro = Alternativa();

        if (!Comer(')'))
        {
            throw new ErroDeExpressao("falta fechar o parêntese", _posicao);
        }

        return capturando ? new No.Grupo(dentro, numero) : dentro;
    }

    private No Escape()
    {
        var onde = _posicao;
        var letra = Pegar();

        var classe = ConjuntoDeCaracteres.PelaLetra(letra);

        if (classe is not null)
        {
            return new No.Classe(classe);
        }

        return letra switch
        {
            'b' => new No.Ancora(TipoDeAncora.Fronteira),
            'B' => new No.Ancora(TipoDeAncora.ForaDeFronteira),
            'n' => new No.Classe(ConjuntoDeCaracteres.De('\n')),
            'r' => new No.Classe(ConjuntoDeCaracteres.De('\r')),
            't' => new No.Classe(ConjuntoDeCaracteres.De('\t')),
            'f' => new No.Classe(ConjuntoDeCaracteres.De('\f')),
            'v' => new No.Classe(ConjuntoDeCaracteres.De('\v')),
            '0' => new No.Classe(ConjuntoDeCaracteres.De('\0')),
            'x' => new No.Classe(ConjuntoDeCaracteres.De(LerHexa(2, onde))),
            'u' => new No.Classe(ConjuntoDeCaracteres.De(LerHexa(4, onde))),
            _ when char.IsAsciiLetterOrDigit(letra) => throw new ErroDeExpressao(
                $"não conheço a fuga '\\{letra}'", onde),
            _ => new No.Classe(ConjuntoDeCaracteres.De(letra)),
        };
    }

    private char LerHexa(int quantos, int onde)
    {
        if (_posicao + quantos > _texto.Length)
        {
            throw new ErroDeExpressao($"a fuga pede {quantos} dígitos hexa", onde);
        }

        var pedaco = _texto.Substring(_posicao, quantos);

        if (!int.TryParse(pedaco, System.Globalization.NumberStyles.HexNumber,
                null, out var valor))
        {
            throw new ErroDeExpressao($"'{pedaco}' não é hexadecimal", onde);
        }

        _posicao += quantos;

        return (char)valor;
    }

    // -- a classe entre colchetes ------------------------------------------

    private ConjuntoDeCaracteres LerClasse()
    {
        var negado = Comer('^');
        var faixas = new List<(char De, char Ate)>();
        var primeiro = true;

        while (true)
        {
            if (Acabou)
            {
                throw new ErroDeExpressao("falta fechar o colchete", _posicao);
            }

            // O `]` logo no começo é um `]` literal. É a regra mais estranha do
            // formato e existe porque não há como escapá-lo em `ed(1)`, de onde
            // a sintaxe veio.
            if (Olhar(']') && !primeiro)
            {
                _posicao++;
                break;
            }

            primeiro = false;

            var onde = _posicao;
            var letra = Pegar();

            if (letra == '\\')
            {
                var deLetra = Pegar();
                var classe = ConjuntoDeCaracteres.PelaLetra(deLetra);

                if (classe is not null)
                {
                    // `[\d]` e `[\D]` dentro de uma classe maior. A negada
                    // precisa das faixas complementares, e não da bandeira.
                    faixas.AddRange(FaixasDe(classe));
                    continue;
                }

                letra = deLetra switch
                {
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    'f' => '\f',
                    'v' => '\v',
                    '0' => '\0',
                    'x' => LerHexa(2, onde),
                    'u' => LerHexa(4, onde),
                    _ when char.IsAsciiLetter(deLetra) => throw new ErroDeExpressao(
                        $"não conheço a fuga '\\{deLetra}' dentro de uma classe", onde),
                    _ => deLetra,
                };
            }

            // Uma faixa. O hífen no fim (`[a-]`) é um hífen literal.
            if (Olhar('-') && _posicao + 1 < _texto.Length && _texto[_posicao + 1] != ']')
            {
                _posicao++;

                var fim = Pegar();

                if (fim == '\\')
                {
                    fim = Pegar() switch
                    {
                        'n' => '\n',
                        'r' => '\r',
                        't' => '\t',
                        _ => _texto[_posicao - 1],
                    };
                }

                if (letra > fim)
                {
                    throw new ErroDeExpressao(
                        $"a faixa {letra}-{fim} está ao contrário", onde);
                }

                faixas.Add((letra, fim));
                continue;
            }

            faixas.Add((letra, letra));
        }

        return ConjuntoDeCaracteres.DeFaixas(faixas, negado);
    }

    /// <summary>As faixas de uma classe com nome, já resolvida a negação.</summary>
    private static IEnumerable<(char De, char Ate)> FaixasDe(ConjuntoDeCaracteres classe)
    {
        // Varrer os 65.536 caracteres uma vez por classe negada dentro de outra
        // classe é feio e é honesto: acontece na análise, uma vez por expressão,
        // e evita uma álgebra de conjuntos inteira para um caso raro.
        var de = -1;

        for (var codigo = 0; codigo <= char.MaxValue; codigo++)
        {
            if (classe.Contem((char)codigo))
            {
                if (de < 0)
                {
                    de = codigo;
                }
            }
            else if (de >= 0)
            {
                yield return ((char)de, (char)(codigo - 1));
                de = -1;
            }
        }

        if (de >= 0)
        {
            yield return ((char)de, char.MaxValue);
        }
    }

    // -- o {n,m} -----------------------------------------------------------

    private bool TentarContagem(out int de, out int ate)
    {
        de = 0;
        ate = -1;

        var guardado = _posicao;

        _posicao++;

        var digitos = LerDigitos();

        if (digitos is null)
        {
            // `{` sem número é uma chave literal. O .NET aceita, e recusar aqui
            // daria uma divergência com o juiz em algo que não é erro de
            // ninguém.
            _posicao = guardado;

            return false;
        }

        de = digitos.Value;

        if (Comer(','))
        {
            var segundo = LerDigitos();

            ate = segundo ?? -1;
        }
        else
        {
            ate = de;
        }

        if (!Comer('}'))
        {
            _posicao = guardado;

            return false;
        }

        return true;
    }

    private int? LerDigitos()
    {
        var comeco = _posicao;

        while (!Acabou && char.IsAsciiDigit(Atual))
        {
            _posicao++;
        }

        if (comeco == _posicao)
        {
            return null;
        }

        return int.Parse(_texto[comeco.._posicao]);
    }

    /// <summary>
    /// Desfaz o <c>{n,m}</c> em cópias.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>a{2,4}</c> vira <c>aa(a(a)?)?</c>, e <c>a{2,}</c> vira <c>aaa*</c>.
    /// É a tradução que toda implementação faz, e ela tem um custo que precisa
    /// ser dito: o programa cresce com o número, não com o tamanho do texto da
    /// expressão. Um <c>a{100000}</c> gera cem mil instruções.
    /// </para>
    /// <para>
    /// Por isso o teto. Sem ele, <c>a{2000000000}</c> é um ataque de duas
    /// dezenas de caracteres — e é exatamente o que derruba servidores que
    /// aceitam expressão de usuário.
    /// </para>
    /// </remarks>
    private const int TetoDeRepeticao = 10_000;

    private static No Contar(No dentro, int de, int ate, int onde)
    {
        if (ate >= 0 && ate < de)
        {
            throw new ErroDeExpressao($"{{{de},{ate}}} está ao contrário", onde);
        }

        if (de > TetoDeRepeticao || ate > TetoDeRepeticao)
        {
            throw new ErroDeExpressao(
                $"repetição de mais de {TetoDeRepeticao} não é aceita aqui: "
                + "ela gera uma instrução por cópia", onde);
        }

        var partes = new List<No>();

        for (var i = 0; i < de; i++)
        {
            partes.Add(dentro);
        }

        if (ate < 0)
        {
            partes.Add(new No.Repeticao(dentro, Guloso: true));
        }
        else
        {
            // Os opcionais aninham em vez de ficarem lado a lado, e isso
            // importa: `a{0,3}` como `a?a?a?` casaria "a" no meio de "ba", e
            // aninhado ele exige os anteriores.
            No? cauda = null;

            for (var i = 0; i < ate - de; i++)
            {
                cauda = new No.Opcional(
                    cauda is null
                        ? dentro
                        : new No.Sequencia([dentro, cauda]),
                    Guloso: true);
            }

            if (cauda is not null)
            {
                partes.Add(cauda);
            }
        }

        return partes.Count switch
        {
            0 => new No.Vazio(),
            1 => partes[0],
            _ => new No.Sequencia(partes),
        };
    }
}

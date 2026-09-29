# regex

Um motor de expressões regulares do zero em C# e .NET 8 — analisador,
compilador e máquina virtual, **sem retrocesso**.

```
$ dotnet regex.dll ver 'a*b'
/a*b/
7 instrução(ões), 0 grupo(s) de captura

   0  guardar  0
   1  dividir  2, 4
   2  casar    [a]
   3  dividir  2, 4
   4  casar    [b]
   5  guardar  1
   6  parar
```

## O juiz

O `System.Text.RegularExpressions` do próprio .NET, rodando de verdade. Cada
caso roda a mesma expressão sobre o mesmo texto nos dois motores e exige que
eles concordem em tudo o que dá para comparar: se casou, onde começou, o que
pegou, e o que cada grupo capturou.

Em **48 mil comparações** com expressões sorteadas pela gramática:

| o que se compara | concordância |
|---|---|
| a correspondência (se casou, onde, e o quê) | **99,990%** |
| as capturas de cada grupo | **99,931%** |

É o único jeito honesto de testar um motor de expressões regulares. As decisões
pequenas são dezenas — qual alternativa vence, até onde o guloso vai, o que um
grupo captura quando repete, o que o `$` faz diante de uma quebra de linha — e
para cada uma há uma resposta plausível que não é a que todo mundo usa.

E havia uma armadilha maior esperando: **as duas famílias de semântica**. O
POSIX escolhe a correspondência *mais longa*; o Perl, o .NET e todos os
descendentes escolhem a que o retrocesso acha *primeiro*. Uma máquina de
Thompson ingênua dá a resposta do POSIX, que está certa e é a outra. Sem o
juiz, eu teria escrito um motor correto que discorda de todo mundo.

## O que o juiz ensinou

**O `$` casa também antes de uma quebra final.**

`/$/` contra `"\n"` casa na posição **0**, e não na 1. Eu tinha escrito a
âncora como "posição igual ao tamanho", que é o que qualquer pessoa escreveria.

A regra vem do Perl, e de antes dele: as ferramentas de linha — `grep`, `sed`,
`awk` — trabalhavam linha a linha, e a quebra final era um detalhe do arquivo,
não do conteúdo. Quando as expressões passaram a rodar sobre textos inteiros, o
comportamento foi mantido para que `/erro$/` continuasse casando uma linha lida
com a quebra dentro.

**A construção do laço tem duas formas, e a diferença é de 0,2 ponto.**

Um `x*` pode fechar com um `pular` de volta para a divisão de entrada, ou com
uma **segunda divisão** de mesmos alvos. As duas parecem certas lendo o código.

| volta do laço | correspondência | capturas |
|---|---|---|
| pulo de volta | 99,785% | 99,537% |
| divisão nova | **99,990%** | **99,931%** |

A razão é sutil: a máquina não segue duas vezes a mesma instrução no mesmo
caractere — senão `(a*)*` giraria para sempre —, e o `pular` de volta cai
exatamente na instrução de onde o laço saiu. Resultado: a iteração que casaria
o vazio morre antes de registrar o que fez. Sendo outra instrução, a divisão
nova deixa **uma** passada vazia acontecer, que é o que um motor com retrocesso
faz.

Sem medir, a escolha teria sido pelo argumento mais convincente — que é a pior
forma de escolher. Há um teste que guarda a comparação, para que alguém que
mexa no compilador veja o número em vez de achar que a instrução a mais é
sobra.

**O que sobra é sempre a mesma família.** As cinco divergências em 48 mil são
todas laços cuja última volta casa o vazio. Não é defeito para corrigir: é o
preço de não ter retrocesso — e é o mesmo preço que compra o que vem a seguir.

## A catástrofe que não acontece

```
(a+)+b   contra   aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
```

Um motor com retrocesso tenta **todas** as formas de dividir as quarenta letras
entre os dois `+` antes de desistir. São 2⁴⁰ caminhos, e cada letra a mais
dobra o número.

Isso tem nome — **ReDoS** — e conta história:

- **20 de julho de 2016**: o Stack Overflow saiu do ar por 34 minutos porque uma
  resposta com muitos espaços no fim encontrou uma expressão dessas.
- **2 de julho de 2019**: a Cloudflare tirou boa parte da internet do ar por 27
  minutos pelo mesmo motivo — uma expressão nova numa regra de firewall, com um
  `.*.*=.*` dentro.

Aqui a mesma expressão roda em microssegundos. **Não por otimização**: por não
haver caminho para explodir. A máquina anda por todos os caminhos ao mesmo
tempo, e "todos os caminhos" cabem no tamanho do programa.

Há um teste que mede as duas coisas: que este motor resolve em menos de 100 ms,
e que o `.NET` estoura o próprio tempo limite de meio segundo no mesmo caso.

## A ideia, em três parágrafos

**Uma expressão regular vira um programa.** É de Ken Thompson, 1968 — ele
compilava para código de máquina do IBM 7094. Aqui são cinco instruções:
`casar`, `pular`, `dividir`, `guardar`, `conferir`. Cinco bastam, e é o que
mantém a máquina com quarenta linhas.

**O `dividir` não escolhe: vai pelos dois.** É a instrução que dispensa o
retrocesso. Em vez de tentar um caminho e voltar, a máquina mantém um conjunto
de posições possíveis no programa e avança todas de uma vez a cada caractere.
Um caractere é lido **uma vez**, e o custo é o tamanho do texto vezes o tamanho
da expressão — sempre, para qualquer expressão.

**Guloso e preguiçoso são a troca de dois argumentos.** A máquina não sabe o
que é ganância. Ela só respeita a ordem em que o `dividir` lista os dois alvos:
no guloso, entrar no corpo vem primeiro; no preguiçoso, sair. Foi a parte que
mais me surpreendeu ao escrever — algo que a documentação de qualquer linguagem
explica com parágrafos e exemplos é, no compilador, uma linha com os argumentos
invertidos.

## A precedência sai da ordem das funções

```
alternativa := sequencia ('|' sequencia)*
sequencia   := repeticao*
repeticao   := atomo ('*' | '+' | '?' | '{n,m}')*
atomo       := literal | classe | '(' alternativa ')' | ancora
```

A alternativa fica no topo porque é o operador mais fraco: em `ab|cd` as duas
sequências inteiras competem, e não `b` com `c`. O quantificador fica embaixo
porque é o mais forte: em `ab*` o asterisco prende só no `b`. Não há tabela de
precedência em lugar nenhum — ela é a ordem em que as quatro funções se chamam.

## Os comandos

```
regex ver    EXPRESSÃO            o programa compilado, instrução por instrução
regex casar  EXPRESSÃO TEXTO      casa, e mostra os grupos
regex achar  EXPRESSÃO [ARQUIVO]  as linhas que casam, como um grep
regex trocar EXPRESSÃO POR TEXTO  troca toda correspondência
```

O erro aponta o caractere:

```
$ dotnet regex.dll ver 'a('
/a(/: posição 2: falta fechar o parêntese
  a(
    ^
```

## Por dentro

| arquivo | o que faz |
|---|---|
| `ConjuntoDeCaracteres.cs` | classes guardadas como faixas — `[^x]` são 65 mil caracteres |
| `Sintaxe.cs` | a árvore em seis formas, e o erro com a posição |
| `Analisador.cs` | descida recursiva em quatro níveis, que **são** a precedência |
| `Programa.cs` | as cinco instruções |
| `Compilador.cs` | a construção de Thompson, com a ganância na ordem dos alvos |
| `Maquina.cs` | a Pike VM |
| `Expressao.cs` | a fachada |
| `ferramentas/Placar` | o que decidiu a construção do laço |

## Rodar

.NET 8. Zero dependências fora do xUnit, e só nos testes.

```
dotnet test testes/Regex.Testes/Regex.Testes.csproj
dotnet run --project ferramentas/Placar/Placar.csproj -- 4000
```

## O que ele não faz

**Não tem retrovisor** (`\1`), e não é falta de vontade: uma expressão com
retrovisor não é mais regular, e casá-la é NP-completo. Um motor que o oferece
está oferecendo a catástrofe junto — os dois vêm do mesmo mecanismo.

Pela mesma razão não há olhar-adiante (`(?=...)`), grupos atômicos nem
quantificadores possessivos. Também não há `\z`, `RegexOptions.Multiline`,
`IgnoreCase`, nomes de grupo, nem classes Unicode por propriedade (`\p{L}`) — e
estes últimos são falta de tempo, não de arquitetura.

E não ganha do .NET em velocidade bruta: o motor do .NET tem décadas de
otimização, compila para IL quando vale a pena, e é mais rápido em quase toda
expressão sadia. A troca é outra — ele tem um caso em que leva horas, e este
não tem.

## Licença

MIT.

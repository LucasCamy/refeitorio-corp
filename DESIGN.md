
# Sistema visual do Refeitório Inteligente

O caixa segue a ordem do atendimento real. Primeiro vem a identificação, depois a refeição e os extras. O pedido fica à direita no desktop. Em telas estreitas, o menu vai para a base e o botão de acesso ao pedido aparece quando há itens.

A interface usa uma base quente e terracota nas ações. Newsreader dá hierarquia ao título e ao total. Inter sustenta os controles e os dados usam algarismos tabulares. Os estados usam ícone, rótulo e cor. As imagens fictícias do cardápio foram geradas para o projeto e exportadas em WebP.

## Referências editáveis

- [Caixa desktop](https://www.figma.com/design/KpX0Fxtba2rzsmuIlVqAl3?node-id=1-24) e [caixa mobile](https://www.figma.com/design/KpX0Fxtba2rzsmuIlVqAl3?node-id=1-110).
- [Produtos](https://www.figma.com/design/KpX0Fxtba2rzsmuIlVqAl3?node-id=1-171), [regras](https://www.figma.com/design/KpX0Fxtba2rzsmuIlVqAl3?node-id=1-205), [relatórios](https://www.figma.com/design/KpX0Fxtba2rzsmuIlVqAl3?node-id=1-239) e [integração](https://www.figma.com/design/KpX0Fxtba2rzsmuIlVqAl3?node-id=1-273).
- [Acesso](https://www.figma.com/design/KpX0Fxtba2rzsmuIlVqAl3?node-id=1-307) e [comprovante](https://www.figma.com/design/KpX0Fxtba2rzsmuIlVqAl3?node-id=1-318).
- [Gastos no desktop](https://www.figma.com/design/KpX0Fxtba2rzsmuIlVqAl3?node-id=10-90), [gastos no mobile](https://www.figma.com/design/KpX0Fxtba2rzsmuIlVqAl3?node-id=10-123), [revisão do pagamento](https://www.figma.com/design/KpX0Fxtba2rzsmuIlVqAl3?node-id=10-165) e [comprovante com parcelas](https://www.figma.com/design/KpX0Fxtba2rzsmuIlVqAl3?node-id=10-191).

O Figma registra hierarquia e estados de referência. As telas implementadas refinam controles e espaçamentos conforme os dados reais e testes do navegador. Até 410 px, o extrato implementado empilha os períodos para preservar a leitura dos valores.

## Tokens e contraste

As razões abaixo foram calculadas a partir da luminância relativa sRGB dos [tokens CSS](apps/web/src/styles/tokens.css). A meta para texto normal é 4,5:1.

| Texto | Claro fundo | Claro painel | Claro agrupado | Claro selecionado | Escuro fundo | Escuro painel | Escuro agrupado | Escuro selecionado |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| `ink` | 14,17 | 15,19 | 12,81 | 12,73 | 14,58 | 12,82 | 11,76 | 10,79 |
| `muted` | 6,25 | 6,70 | 5,65 | 5,62 | 9,31 | 8,18 | 7,51 | 6,89 |
| `action` | 6,48 | 6,95 | 5,86 | 5,83 | 9,28 | 8,15 | 7,48 | 6,86 |
| `success` | 7,44 | 7,98 | 6,73 | 6,69 | 10,31 | 9,07 | 8,32 | 7,63 |
| `warning` | 7,09 | 7,59 | 6,41 | 6,37 | 10,81 | 9,50 | 8,72 | 8,00 |
| `destructive` | 6,66 | 7,13 | 6,02 | 5,98 | 9,77 | 8,59 | 7,88 | 7,23 |

- Botão primário, `on-action` sobre `action` · 7,06:1 no claro e 9,13:1 no escuro.
- Status de sucesso, alerta e erro sobre suas superfícies · 6,83 / 6,60 / 6,06 no claro e 6,56 / 7,22 / 6,75 no escuro.
- Texto do prato, `on-night` e `muted-night` sobre `night` · 13,69:1 e 10,47:1.

Os controles móveis têm alvos de pelo menos 44 px, campos de 16 px, foco visível e diálogos nativos. A versão com movimento reduzido é coberta pelo teste Playwright.

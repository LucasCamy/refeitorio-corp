
# Requisitos e plano do MVP

## Escopo e critérios de aceite

| Prioridade | Perfil | Fluxo | Critério |
|---|---|---|---|
| P0 | Operador | Login e identificação assistida por RF ou leitor de teclado | Exibe nome, setor e elegibilidade, limita o retorno ao necessário e bloqueia inativos. Somente um caixa pode manter identificação ativa por funcionário. |
| P0 | Operador | Cotar e confirmar almoço e extras | A cotação usa preços vigentes do servidor. O pedido registra desconto, pagamento, operador e caixa atomicamente. Repetir a confirmação com a mesma chave devolve o mesmo comprovante. |
| P0 | Operador | Registrar almoço em folha e extras no caixa | O servidor calcula cada parcela após o subsídio. Cartão e PIX exigem confirmação e referência da operação externa. Dinheiro é registrado como recebido. |
| P0 | Operador | Consultar gastos do funcionário identificado | Mostra total de hoje e do mês, parcelas em folha e caixa e itens extras consumidos na unidade. |
| P0 | Operador | Comprovante | O caixa exibe comprovante digital e oferece impressão via navegador. |
| P0 | Gerente e administrador | Relatório e conciliação | Resumo e lista do período persistidos, com abatimento de estornos. Verifique por método contra terminal externo ou folha. |
| P0 | Administrador | Produtos e regra de almoço | Cria e edita extras, disponibilidade, preço, código de barras e desconto. Regra configurável de horário, subsídio e limites. |
| P0 | Gerente e administrador | Estorno | Confirma motivo, preserva original e enfileira ajustes externos. Pagamento externo precisa ser devolvido e referenciado pelo operador autorizado. |
| P0 | Administrador | Integração | Consulta estado, reenvia falhas definitivas e verifica a distinção de entrega pendente ou concluída. |
| P1 | RH | Provedor real e sincronização | Validar OAuth 2.0, contrato, paginação, reconciliação, provisionamento e revogação com o fornecedor. |
| P1 | Funcionário | Extrato autenticado | Confirma a própria identidade por SSO e visualiza os próprios consumos. |
| P2 | Funcionário | Totem, saldo pré-pago, uso offline, notificações | Depende de regras e aprovação operacional para autenticação, limites offline e sincronização. |

## Requisitos não funcionais

- Isolamento de empresa e unidade no servidor. Operações financeiras exigem autenticação e RBAC. RF identifica o funcionário em caixa assistido e o crachá carrega token aleatório.
- Limite de requisições e de tamanho de corpo. Auditoria de acesso funcional e eventos financeiros, com acesso administrativo restrito e dados mínimos.
- Fuso de referência do piloto `America/Cuiaba`. Instantes em UTC e data comercial calculada pelo servidor.
- Valores em centavos inteiros. PostgreSQL garante integridade referencial e atômica. Deduplicação por chave de confirmação, com rejeição de alteração no conteúdo original.
- Fluxos em português brasileiro, foco por teclado, contraste AA, alvos de toque de pelo menos 44 px no mobile e respeito à preferência de movimento reduzido.
- Integração com timeout, fila persistente e reprocessamento. Sandbox com dados fictícios. Contratos, testes e procedimento de verificação versionados.

## Sequência de entrega

1. Regras e contratos, design de caixa e persistência local.
2. Percurso vertical de login, identificação, cotação, confirmação, comprovante e baixa.
3. Catálogo, regra, relatório, estorno e operação de fila.
4. Validação do MVP no navegador e no banco. Homologação com RH e terminal depende dos fornecedores e políticas da empresa.


# Fluxos de tela

| Perfil | Caminho principal | Tratamento de exceções |
|---|---|---|
| Funcionário em caixa assistido | Apresenta RF ou crachá → confere dados e pode consultar gastos com o atendente → confirma almoço em folha e extras no caixa → recebe comprovante | Quando já houve baixa, o caixa indica a situação e pode registrar extras. Funcionário inativo ou sem benefício tem o pedido bloqueado. |
| Operador | Entrar → frente de caixa → identificar → selecionar almoço e extras → conferir cotação → escolher liquidação → confirmar → imprimir ou iniciar novo atendimento | Identificação concorrente mostra ocupação. Mudança de regra, preço ou validade pede conferência. Em erro de rede na confirmação, o caixa reenvia a mesma chave de idempotência. |
| Administrador | Produtos → editar cadastro. Regras → ajustar valores, janelas e limites. Relatórios → filtrar por período. Integração RH → verificar fila e reprocessar falhas. | Mensagens no contexto do formulário. Estorno pede justificativa e, para caixa externo, referência da devolução. |
| Funcionário em autosserviço futuro | Autenticar por identidade corporativa → consultar extrato → abrir comprovante próprio | Restringir ao titular autenticado e revisar dados exibidos com RH. |

O botão primário acompanha a ação atual no caixa. Campos de identificação mantêm foco por teclado. O comprovante exibe pagamento e valor em reais e mantém um identificador para conciliação.

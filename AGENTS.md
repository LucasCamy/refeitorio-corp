
# Refeitório Inteligente

Aplicação de caixa corporativo em português brasileiro, com React e TypeScript, API ASP.NET Core e PostgreSQL. O primeiro piloto usa uma unidade e integra folha por sandbox.

## Organização

- `apps/web` contém a interface, com tokens em `src/styles/tokens.css` e referência visual em [DESIGN.md](DESIGN.md).
- `apps/api` contém contratos REST, domínio, persistência e o adaptador de RH.
- `database/migrations` contém SQL versionado. O checkout e o outbox compartilham a mesma transação de banco.
- `tests` contém testes do domínio, contrato REST e cenários transacionais.

## Trabalho e validação

Siga os módulos de fluxo, frontend, backend, persistência e documentação do [Agent Project Kit](https://github.com/LucasCamy/agent-project-kit/tree/c8067fbbfdeb73585b12d7dae533661b369a0f5a). Estruture mudanças de interface no Figma e valide os fluxos no Playwright. Confira os comandos e o mapa documental no [README](README.md).

Use DTOs, Problem Details, queries parametrizadas e escopo de empresa e unidade no servidor. Mantenha montantes em centavos de BRL, UTC para instantes e data comercial no fuso da empresa. Preserve itens, subsídios e pagamentos históricos. Use estornos auditáveis e chaves de idempotência estáveis. Separe revisão e aplicação de migrations. Use somente fixtures fictícias em desenvolvimento.


# Refeitório Inteligente

Frente de caixa e gestão de um restaurante corporativo. O MVP cobre identificação assistida, baixa do almoço, catálogo, checkout, comprovante, relatórios, estorno e fila de integração com RH.

## Rodar no Windows

Requisitos: .NET 10 SDK, Node.js 24, Docker Desktop com Compose e PowerShell. Com o Docker iniciado, execute na raiz do projeto:

```powershell
./scripts/dev.ps1 setup
```

Em terminais separados:

```powershell
./scripts/dev.ps1 api
./scripts/dev.ps1 web
```

Abra [a aplicação](http://127.0.0.1:5173/) ou [o Swagger](http://localhost:5088/swagger). O setup grava credenciais de desenvolvimento em `.env`, ignorado pelo Git. Use a senha `SANDBOX_PASSWORD` desse arquivo com `operador`, `caixa2`, `gerente` ou `admin`. O perfil `admin` abre todas as telas. Reutilize o mesmo `.env` entre execuções pois o volume PostgreSQL mantém as credenciais originais. Antes de rodar `setup` novamente no Windows, encerre os processos `api` e `web` para liberar os arquivos em uso.

| Fixture | Comportamento |
|---|---|
| RF `1001` ou crachá `RF-BADGE-77a64bab-fb5b-449b-9e48-931c02355b80` | Marina Costa, elegível para folha |
| RF `1002` | Rafael Nunes, elegível para folha |
| RF `1003` | Funcionária inativa |
| RF `1004` | Funcionário habilitado para extras no caixa e para almoço quando integralmente subsidiado |

Comece identificando um RF. **Consultar gastos** mostra o consumo anterior. Inclua o almoço e um extra e revise o pedido. O almoço vai para folha quando há valor após o subsídio, e o extra é pago no caixa. A autorização de cartão e PIX ocorre no terminal ou app externo. Em **Relatórios**, um gerente pode abrir o comprovante e estornar o pedido. Em **Integração RH**, um administrador pode acompanhar o envio ao sandbox.

## Validação local

```powershell
./scripts/dev.ps1 test
npm --prefix apps/web run build
npm --prefix apps/web exec playwright -- install chromium
npm --prefix apps/web run test:e2e
```

A suíte .NET usa um banco isolado criado no PostgreSQL local e o elimina ao concluir. A suíte de navegador usa o sandbox em execução em 5173, registra um pedido fictício no RF 1002 e o estorna. Execute-a em um sandbox sem baixa ativa nesse RF. Opcionalmente indique uma pasta de evidência absoluta em `E2E_SCREENSHOTS`. Evite compilar em Debug enquanto a API anterior estiver usando os binários dessa configuração no Windows.

O comando `--migrate` aplica migrations versionadas explicitamente. A API em operação não migra automaticamente. `--seed` opera em Development + Sandbox e requer senha de teste. Produção exige banco provisionado, `Jwt__SigningKey` em Base64, `ConnectionStrings__Refeitorio`, HTTPS, host permitido, contas de staff provisionadas e configuração do adaptador descrita no contrato. O PostgreSQL de Compose escuta somente na interface de loopback.

## Documentação

- [Requisitos e priorização](docs/requisitos-e-mvp.md)
- [Arquitetura e modelo de dados](docs/arquitetura.md)
- [Contratos REST e integração RH](docs/api.md)
- [Fluxos de tela](docs/fluxos.md)
- [Subsídio, limites, estorno e conciliação](docs/regras-e-conciliacao.md)
- [Segurança, privacidade e testes](docs/seguranca-lgpd-testes.md)
- [Direção visual e Figma](DESIGN.md)
- [Decisão de arquitetura do piloto](docs/adr/0001-arquitetura-do-piloto.md)
- [Decisão sobre folha e caixa](docs/adr/0002-almoco-em-folha-e-extras-no-caixa.md)


import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { ArrowLeft, ArrowRight, Boxes, CalendarDays, CircleAlert, CircleCheck, CircleDashed, ClipboardList, Pencil, Plus, Radio, RefreshCw, SquareX } from 'lucide-react'
import { body, categories, currency, localTime, message, methods } from './api'
import type { Catalog, Integration, MealRule, Product, Receipt, Report, ReportOrder, Request } from './api'
import { PageHeading } from './PageHeading'
import { Modal } from './Modal'
import { ReceiptView } from './Checkout'

function useResource<T>(api: Request, path: string) {
  const [data, setData] = useState<T | null>(null)
  const [error, setError] = useState('')
  const [attempt, setAttempt] = useState(0)
  const [loading, setLoading] = useState(true)
  useEffect(() => {
    const controller = new AbortController()
    api<T>(path, { signal: controller.signal }).then(value => {
      if (controller.signal.aborted) return
      setData(value); setError('')
    }).catch(cause => { if (!controller.signal.aborted) setError(message(cause)) })
      .finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => controller.abort()
  }, [api, path, attempt])
  return { data, error, loading, refresh: () => { setLoading(true); setError(''); setAttempt(x => x + 1) }, setData }
}

function InlineError({ error }: { error: string }) {
  return error ? <p className="error" role="alert"><CircleAlert size={16} />{error}</p> : null
}

function LoadState({ loading, error, refresh }: { loading: boolean; error: string; refresh: () => void }) {
  return <>{loading && <div className="skeleton panel-placeholder" role="status" aria-label="Carregando" />}
    {error && <div className="notice error" role="alert"><CircleAlert size={18} />{error}
      <button className="button secondary" onClick={refresh}>Tentar novamente</button></div>}</>
}

function Status({ kind, label }: { kind: 'success' | 'warning' | 'danger'; label: string }) {
  const Icon = kind === 'success' ? CircleCheck : kind === 'warning' ? CircleDashed : SquareX
  return <span className={'status ' + kind}><Icon size={15} />{label}</span>
}

const decimal = (cents: number) => (cents / 100).toFixed(2)
const cents = (data: FormData, key: string) => Math.round(Number(data.get(key)) * 100)
const nullableCents = (data: FormData, key: string) => (data.get(key) === '' ? null : cents(data, key))

function ProductForm({ entry, api, close, saved }: { entry: Product | null; api: Request; close: () => void; saved: (p: Product) => void }) {
  const [pending, setPending] = useState(false)
  const [error, setError] = useState('')
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const data = new FormData(event.currentTarget)
    setPending(true); setError('')
    try {
      saved(await api<Product>(entry ? '/products/' + entry.id : '/products', body({
        name: data.get('name'), description: data.get('description'), category: data.get('category'),
        barcode: data.get('barcode') || null, imageKey: data.get('imageKey') || null,
        priceCents: cents(data, 'price'), subsidyCents: cents(data, 'subsidy'), available: data.get('available') === 'on',
        version: entry?.version ?? null
      }, entry ? 'PUT' : 'POST')))
    } catch (cause) { setError(message(cause)) }
    finally { setPending(false) }
  }
  return <Modal title={entry ? 'Editar produto' : 'Novo produto'} close={close} busy={pending}>
    <form className="form-stack" onSubmit={submit}>
      <label>Nome<input name="name" defaultValue={entry?.name} required maxLength={80} /></label>
      <label>Descrição<input name="description" defaultValue={entry?.description} maxLength={200} /></label>
      <div className="form-row">
        <label>Categoria<select name="category" defaultValue={entry?.category ?? 'beverage'}>{Object.entries(categories).map(([key, label]) => <option key={key} value={key}>{label}</option>)}</select></label>
        <label>Código de barras<input name="barcode" defaultValue={entry?.barcode ?? ''} maxLength={40} /></label>
      </div>
      <div className="form-row">
        <label>Preço (R$)<input name="price" type="number" min="0" max="10000" step="0.01" defaultValue={decimal(entry?.priceCents ?? 0)} required /></label>
        <label>Subsídio por unidade (R$)<input name="subsidy" type="number" min="0" max="10000" step="0.01" defaultValue={decimal(entry?.subsidyCents ?? 0)} required /></label>
      </div>
      <label>Imagem no catálogo<select name="imageKey" defaultValue={entry?.imageKey ?? ''}>
        <option value="">Sem imagem</option><option value="juice">Suco</option><option value="cola">Refrigerante</option>
        <option value="flan">Pudim</option><option value="brownie">Brownie</option><option value="takeout">Marmitex</option>
      </select></label>
      <label className="check-row"><input type="checkbox" name="available" defaultChecked={entry?.available ?? true} /> Disponível no caixa</label>
      <InlineError error={error} />
      <div className="form-actions"><button type="button" className="button secondary" disabled={pending} onClick={close}>Cancelar</button>
        <button className="button primary" disabled={pending}>{pending ? 'Salvando…' : 'Salvar produto'}</button></div>
    </form>
  </Modal>
}

export function ProductsPage({ api }: { api: Request }) {
  const resource = useResource<Product[]>(api, '/products')
  const [editing, setEditing] = useState<Product | null | undefined>(undefined)
  const [search, setSearch] = useState('')
  const [saved, setSaved] = useState('')
  const visible = resource.data?.filter(p => p.name.toLowerCase().includes(search.toLowerCase().trim()) ||
    p.barcode === search.trim()) ?? []
  return <>
    <PageHeading title="Produtos" subtitle="Gerencie os itens extras disponíveis no caixa.">
      <button className="button primary" onClick={() => { setSaved(''); setEditing(null) }}><Plus size={18} /> Novo produto</button>
    </PageHeading>
    {saved && <p className="notice success" role="status"><CircleCheck size={17} />{saved}</p>}
    <LoadState {...resource} />
    {resource.data && <section className="panel management-panel">
      <div className="management-tools"><label>Buscar no catálogo<input placeholder="Nome ou código de barras"
        value={search} onChange={e => setSearch(e.target.value)} /></label><span>{visible.length} produtos</span></div>
      {visible.length ? <table className="data-table"><thead><tr><th>Produto</th><th>Categoria</th><th>Preço</th><th>Disponibilidade</th><th>Ações</th></tr></thead>
        <tbody>{visible.map(product => <tr key={product.id}><td data-label="Produto"><div className="table-product">
          {product.imageKey ? <img src={'/menu/' + product.imageKey + '.webp'} alt="" /> : <span className="section-icon"><Boxes size={17} /></span>}
          <div><strong>{product.name}</strong>{product.description && <small>{product.description}</small>}</div></div></td>
          <td data-label="Categoria">{categories[product.category]}</td><td data-label="Preço">{currency(product.priceCents)}</td>
          <td data-label="Disponibilidade"><Status kind={product.available ? 'success' : 'warning'} label={product.available ? 'Disponível' : 'Indisponível'} /></td>
          <td data-label="Ações"><button className="button secondary compact" aria-label={'Editar ' + product.name} onClick={() => { setSaved(''); setEditing(product) }}>
            <Pencil size={14} /> Editar</button></td></tr>)}</tbody></table> :
        <div className="empty"><Boxes size={25} /><strong>Nenhum produto encontrado</strong>
          <button className="button secondary" onClick={() => search ? setSearch('') : setEditing(null)}>{search ? 'Limpar busca' : 'Criar produto'}</button></div>}
    </section>}
    {editing !== undefined && <ProductForm entry={editing} api={api} close={() => setEditing(undefined)} saved={product => {
      setEditing(undefined); setSaved(product.name + ' salvo.'); resource.refresh()
    }} />}
  </>
}

function RuleForm({ rule, timezone, api, saved }: { rule: MealRule; timezone: string; api: Request; saved: (value: MealRule) => void }) {
  const [pending, setPending] = useState(false)
  const [error, setError] = useState('')
  const [ok, setOk] = useState(false)
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const data = new FormData(event.currentTarget)
    const time = (name: string, previous: string) => data.get(name) === previous.slice(0, 5) ? previous : data.get(name) + ':00'
    setPending(true); setError(''); setOk(false)
    try {
      saved(await api<MealRule>('/meal-rule', body({
        name: data.get('name'), description: data.get('description'), startsAt: time('start', rule.startsAt),
        endsAt: time('end', rule.endsAt), priceCents: cents(data, 'price'), subsidyCents: cents(data, 'subsidy'),
        mealLimit: Number(data.get('limit')), dailyLimitCents: nullableCents(data, 'daily'),
        monthlyLimitCents: nullableCents(data, 'monthly'), version: rule.version
      }, 'PUT')))
      setOk(true)
    } catch (cause) { setError(message(cause)) }
    finally { setPending(false) }
  }
  return <section className="panel management-panel rule-panel">
    <div className="panel-lead"><span className="section-icon"><ClipboardList size={19} /></span>
      <div><h2>Turno de almoço</h2><p>Os horários seguem o fuso {timezone}.</p></div></div>
    <form className="form-stack" onSubmit={submit}>
      <label>Nome da refeição<input name="name" required maxLength={80} defaultValue={rule.name} /></label>
      <label>Descrição do prato<input name="description" maxLength={200} defaultValue={rule.description} /></label>
      <div className="form-row">
        <label>Início do atendimento<input required type="time" name="start" defaultValue={rule.startsAt.slice(0, 5)} /></label>
        <label>Fim do atendimento<input required type="time" name="end" defaultValue={rule.endsAt.slice(0, 5)} /></label>
      </div>
      <div className="form-row">
        <label>Preço do almoço (R$)<input type="number" name="price" required min="0" max="10000" step="0.01" defaultValue={decimal(rule.priceCents)} /></label>
        <label>Subsídio da empresa (R$)<input type="number" name="subsidy" required min="0" max="10000" step="0.01" defaultValue={decimal(rule.subsidyCents)} /></label>
      </div>
      <label>Almoços por funcionário por dia<input name="limit" type="number" required min="1" max="10" defaultValue={rule.mealLimit} /></label>
      <div className="form-row">
        <label>Limite diário (R$)<input type="number" name="daily" min="0" max="1000000" step="0.01" defaultValue={rule.dailyLimitCents === null ? '' : decimal(rule.dailyLimitCents)} /></label>
        <label>Limite mensal (R$)<input type="number" name="monthly" min="0" max="1000000" step="0.01" defaultValue={rule.monthlyLimitCents === null ? '' : decimal(rule.monthlyLimitCents)} /></label>
      </div>
      <small className="muted">Deixe os limites em reais vazios para permitir consumo sem esse limite.</small>
      <InlineError error={error} />
      {ok && <p className="notice success" role="status"><CircleCheck size={17} /> Regra atualizada.</p>}
      <div className="form-actions"><button className="button primary" disabled={pending}>{pending ? 'Salvando…' : 'Salvar regra'}</button></div>
    </form>
  </section>
}

export function RulesPage({ api }: { api: Request }) {
  const resource = useResource<Catalog>(api, '/checkout/catalog')
  return <>
    <PageHeading title="Regras de almoço" subtitle="Configure horário, subsídio e limites de atendimento." />
    <LoadState {...resource} />
    {resource.data && <RuleForm rule={resource.data.rule} timezone={resource.data.timezone} api={api}
      saved={rule => resource.setData(current => current ? { ...current, rule } : current)} />}
  </>
}

function OrderDetail({ order, api, timezone, refresh, close }: { order: ReportOrder; api: Request; timezone: string; refresh: () => void; close: () => void }) {
  const resource = useResource<Receipt>(api, '/orders/' + order.id)
  const [reversing, setReversing] = useState(false)
  const [pending, setPending] = useState(false)
  const [error, setError] = useState('')
  async function reverse(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const data = Object.fromEntries(new FormData(event.currentTarget))
    setPending(true); setError('')
    try {
      resource.setData(await api<Receipt>('/orders/' + order.id + '/reverse', body(data)))
      setReversing(false)
      refresh()
    } catch (cause) { setError(message(cause)) }
    finally { setPending(false) }
  }
  return <Modal title="Detalhe do atendimento" close={close} busy={pending}>
    <LoadState {...resource} />
    {resource.data && <><ReceiptView receipt={resource.data} timezone={timezone} />
      {resource.data.status === 'confirmed' && (reversing ? <form className="form-stack reversal-form no-print" onSubmit={reverse}>
        <label>Motivo do estorno<textarea name="reason" required minLength={8} maxLength={300} rows={3} /></label>
        {resource.data.payments.some(payment => ['cash', 'card', 'pix'].includes(payment.method)) && <label>Referência da devolução
          <input name="refundReference" required maxLength={100} /></label>}
        <InlineError error={error} />
        <div className="form-actions"><button type="button" className="button secondary" disabled={pending} onClick={() => setReversing(false)}>Cancelar</button>
          <button className="button primary" disabled={pending}>{pending ? 'Estornando…' : 'Confirmar estorno'}</button></div>
      </form> : <button className="button secondary no-print" type="button" onClick={() => setReversing(true)}>Estornar atendimento</button>)}</>}
  </Modal>
}

export function ReportsPage({ api, timezone }: { api: Request; timezone: string }) {
  const [period, setPeriod] = useState({ from: '', to: '', page: 1 })
  const query = new URLSearchParams({ page: String(period.page) })
  if (period.from) query.set('from', period.from)
  if (period.to) query.set('to', period.to)
  const resource = useResource<Report>(api, '/reports/orders?' + query)
  const [order, setOrder] = useState<ReportOrder | null>(null)
  const report = resource.data
  function filter(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const data = new FormData(event.currentTarget)
    const next = { from: String(data.get('from')), to: String(data.get('to')), page: 1 }
    if (next.from === period.from && next.to === period.to && period.page === 1) resource.refresh()
    else setPeriod(next)
  }
  return <>
    <PageHeading title="Relatórios" subtitle="Acompanhe consumo, pagamentos e estornos por período." />
    <section className="panel management-panel">
      <form className="report-filters" onSubmit={filter} key={report?.from + '/' + report?.to}>
        <label>De<input name="from" type="date" required defaultValue={report?.from} /></label>
        <label>Até<input name="to" type="date" required defaultValue={report?.to} /></label>
        <button className="button primary" disabled={resource.loading}><CalendarDays size={17} /> Aplicar período</button>
      </form>
    </section>
    <LoadState {...resource} />
    {report && <>
      <div className="metric-grid">
        {[['Atendimentos', report.summary.orders], ['Almoços', report.summary.meals],
          ['Subsídio', currency(report.summary.subsidyCents)], ['Receita líquida', currency(report.summary.totalCents)]].map(([label, value]) =>
          <div className="panel metric" key={label}><span>{label}</span><strong>{value}</strong></div>)}
      </div>
      <section className="panel management-panel">
        <div className="management-tools"><div><h2>Conciliação por pagamento</h2><p>{report.summary.reversals} estornos no período</p></div></div>
        {report.methods.length ? <ul className="method-list">{report.methods.map(item =>
          <li key={item.method}><span>{methods[item.method]} <small>{item.orders} atend.</small></span><strong>{currency(item.totalCents)}</strong></li>)}</ul>
          : <p className="muted">Nenhum pagamento confirmado neste período.</p>}
      </section>
      <section className="panel management-panel">
        <div className="management-tools"><h2>Atendimentos</h2></div>
        {report.orders.length ? <table className="data-table"><thead><tr><th>Data e hora</th><th>Funcionário</th><th>Pagamento</th><th>Valor</th><th>Situação</th><th>Ações</th></tr></thead>
          <tbody>{report.orders.map(item => <tr key={item.id}><td data-label="Data">{localTime(item.createdAt, timezone)}</td>
            <td data-label="Funcionário">{item.employee}</td><td data-label="Pagamento">{item.paymentMethods.map(method => methods[method]).join(' + ')}{item.reference && <small>Ref. {item.reference}</small>}</td>
            <td data-label="Valor">{currency(item.totalCents)}</td><td data-label="Situação"><Status kind={item.status === 'confirmed' ? 'success' : 'danger'} label={item.status === 'confirmed' ? 'Registrado' : 'Estornado'} /></td>
            <td data-label="Ações"><button className="button secondary compact" onClick={() => setOrder(item)}>Detalhes</button></td></tr>)}</tbody></table> :
          <div className="empty"><CalendarDays size={25} /><strong>Nenhum atendimento nesta página</strong><p>Ajuste o período ou registre um pedido no caixa.</p></div>}
        <div className="pagination"><button className="button secondary compact" disabled={period.page === 1 || resource.loading} onClick={() => setPeriod({ ...period, page: period.page - 1 })}><ArrowLeft size={16} /> Anterior</button>
          <span>Página {report.page}</span><button className="button secondary compact" disabled={report.orders.length < 30 || resource.loading} onClick={() => setPeriod({ ...period, page: period.page + 1 })}>Próxima <ArrowRight size={16} /></button></div>
      </section>
    </>}
    {order && <OrderDetail order={order} api={api} timezone={timezone} refresh={resource.refresh} close={() => setOrder(null)} />}
  </>
}

const eventNames: Record<string, string> = { meal: 'Baixa de almoço', consumption: 'Consumo de extras', payroll: 'Desconto em folha' }
const eventStatus = { pending: ['warning', 'Pendente'], delivered: ['success', 'Entregue'], failed: ['danger', 'Falhou'] } as const

export function IntegrationPage({ api, timezone }: { api: Request; timezone: string }) {
  const resource = useResource<Integration>(api, '/integration/events')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState<string | null>(null)
  async function retry(id: string) {
    setBusy(id); setError('')
    try { await api('/integration/events/' + id + '/retry', { method: 'POST' }); resource.refresh() }
    catch (cause) { setError(message(cause)) }
    finally { setBusy(null) }
  }
  return <>
    <PageHeading title="Integração RH" subtitle="Acompanhe a entrega de lançamentos do refeitório.">
      <button className="button secondary" disabled={resource.loading} onClick={resource.refresh}><RefreshCw size={17} /> Atualizar fila</button>
    </PageHeading>
    <LoadState {...resource} />
    <InlineError error={error} />
    {resource.data && <>
      <div className="notice"><Radio size={18} />{resource.data.mode === 'Sandbox' ? 'Ambiente de sandbox com dados fictícios' : 'Provedor externo configurado'}</div>
      <div className="metric-grid">
        {([['Pendente', 'pending'], ['Entregue', 'delivered'], ['Falhou', 'failed']] as const).map(([label, state]) =>
          <div className="panel metric" key={state}><Status kind={eventStatus[state][0]} label={label} /><strong>{resource.data?.[state]}</strong></div>)}
      </div>
      <section className="panel management-panel">
        <div className="management-tools"><h2>Últimos lançamentos</h2></div>
        {resource.data.events.length ? <table className="data-table"><thead><tr><th>Tipo</th><th>Situação</th><th>Tentativas</th><th>Entrega</th><th>Ações</th></tr></thead>
          <tbody>{resource.data.events.map(event => <tr key={event.id}>
            <td data-label="Tipo"><strong>{eventNames[event.type] ?? event.type}</strong><small>Pedido {event.orderId.slice(0, 8)}</small></td>
            <td data-label="Situação"><Status kind={eventStatus[event.status][0]} label={eventStatus[event.status][1]} />{event.errorCode && <small>{event.errorCode}</small>}</td>
            <td data-label="Tentativas">{event.attempts}</td><td data-label="Entrega">{event.deliveredAt ? localTime(event.deliveredAt, timezone) : 'Aguardando'}</td>
            <td data-label="Ações">{event.status === 'failed' && <button className="button secondary compact" disabled={!!busy} onClick={() => retry(event.id)}>
              {busy === event.id ? 'Enviando…' : 'Reprocessar'}</button>}</td>
          </tr>)}</tbody></table> : <div className="empty"><Radio size={25} /><strong>Nenhum lançamento na fila</strong><p>As baixas e os consumos aparecem após o primeiro atendimento.</p></div>}
      </section>
    </>}
  </>
}

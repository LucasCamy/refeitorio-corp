import { useEffect, useState } from 'react'
import { ArrowLeft, ArrowRight, CircleAlert, ReceiptText } from 'lucide-react'
import { currency, localTime, message } from './api'
import type { Identification, Request, SpendingHistory, SpendingTotals } from './api'
import { Modal } from './Modal'

function Period({ title, totals }: { title: string; totals: SpendingTotals }) {
  return <section className="spending-period">
    <h3>{title}</h3>
    <p className="spending-total"><span>Total gasto</span><strong>{currency(totals.totalCents)}</strong></p>
    <dl><div><dt>Desconto em folha</dt><dd>{currency(totals.payrollCents)}</dd></div>
      <div><dt>Pago no caixa</dt><dd>{currency(totals.registerCents)}</dd></div></dl>
  </section>
}

export function SpendingDialog({ api, identity, timezone, close }: { api: Request; identity: Identification; timezone: string; close: () => void }) {
  const [result, setResult] = useState<SpendingHistory | null>(null)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(true)
  const [page, setPage] = useState(1)
  const [attempt, setAttempt] = useState(0)

  useEffect(() => {
    const controller = new AbortController()
    api<SpendingHistory>('/checkout/sessions/' + identity.checkoutToken + '/spending?page=' + page, { signal: controller.signal })
      .then(value => { if (!controller.signal.aborted) setResult(value) })
      .catch(cause => { if (!controller.signal.aborted) setError(message(cause)) })
      .finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => controller.abort()
  }, [api, identity.checkoutToken, page, attempt])

  function load(change: () => void) {
    setError(''); setLoading(true); change()
  }

  return <Modal title="Gastos do funcionário" close={close}>
    <p className="spending-person"><strong>{identity.employee.name}</strong> · RF {identity.employee.rf}{result && ' · ' + result.unit}</p>
    {loading && !result && <div className="skeleton panel-placeholder" role="status" aria-label="Carregando gastos" />}
    {error && <div className="notice error" role="alert"><CircleAlert size={16} />{error}
      <button type="button" className="button secondary" onClick={() => load(() => setAttempt(value => value + 1))}>Tentar novamente</button></div>}
    {result && !error && <div className="spending-content" aria-busy={loading}>
      <div className="spending-periods"><Period title="Hoje" totals={result.today} /><Period title="Neste mês" totals={result.month} /></div>
      <section aria-labelledby="spent-extras"><h3 id="spent-extras">Itens extras neste mês</h3>
        {result.extras.length ? <ul className="spending-items">{result.extras.map(item =>
          <li key={item.orderId + '/' + item.position}><div><strong>{item.quantity} × {item.name}</strong>
            <small>{localTime(item.createdAt, timezone)} · {item.terminal}</small></div><strong>{currency(item.netCents)}</strong></li>)}</ul> :
          <div className="empty small"><ReceiptText size={23} /><strong>{page === 1 ? 'Nenhum extra registrado neste mês' : 'Nenhum extra nesta página'}</strong></div>}
        {(page > 1 || result.hasMore) && <div className="pagination">
          <button type="button" className="button secondary compact" disabled={loading || page === 1} onClick={() => load(() => setPage(page - 1))}><ArrowLeft size={16} /> Anterior</button>
          <span>Página {page}</span>
          <button type="button" className="button secondary compact" disabled={loading || !result.hasMore} onClick={() => load(() => setPage(page + 1))}>Próxima <ArrowRight size={16} /></button>
        </div>}
      </section>
    </div>}
  </Modal>
}

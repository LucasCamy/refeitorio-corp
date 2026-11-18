
import { useCallback, useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import {
  ArrowRight, BadgeCheck, BarChart3, Boxes, ClipboardList, CookingPot, DoorOpen, LogOut, Moon,
  Radio, Sun, Utensils, WifiOff
} from 'lucide-react'
import { ApiError, body, initials, message, request } from './api'
import type { Request, Role, Session } from './api'
import { Checkout } from './Checkout'
import { IntegrationPage, ProductsPage, ReportsPage, RulesPage } from './Management'

const tabs = [
  { id: 'checkout', label: 'Frente de caixa', short: 'Caixa', icon: CookingPot },
  { id: 'products', label: 'Produtos', short: 'Produtos', icon: Boxes },
  { id: 'rules', label: 'Regras de almoço', short: 'Regras', icon: ClipboardList },
  { id: 'reports', label: 'Relatórios', short: 'Relatórios', icon: BarChart3 },
  { id: 'integration', label: 'Integração RH', short: 'RH', icon: Radio }
] as const

type Tab = (typeof tabs)[number]['id']
const roles: Record<Role, string> = { operator: 'Operador de caixa', manager: 'Gerência', admin: 'Administração' }
const allowed = (tab: Tab, role: Role) => tab === 'checkout' || role === 'admin' || (role === 'manager' && tab === 'reports')

function Brand() {
  return <div className="brand"><span className="brand-mark"><Utensils size={20} /></span>
    <span>Refeitório <small>Inteligente</small></span></div>
}

function Login({ onLogin, notice }: { onLogin: (session: Session) => void; notice: string }) {
  const [pending, setPending] = useState(false)
  const [error, setError] = useState('')
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = Object.fromEntries(new FormData(event.currentTarget))
    setPending(true)
    setError('')
    try { onLogin(await request<Session>('/auth/login', body(form))) }
    catch (cause) { setError(message(cause)) }
    finally { setPending(false) }
  }
  return <main className="login">
    <section className="login-side"><Brand /><div><h1>Refeitório<br />Inteligente.</h1>
      <p>Caixa e gestão do restaurante corporativo.</p></div><DoorOpen size={22} aria-hidden="true" /></section>
    <section className="login-form">
      <span className="small-label">Acesso ao sistema</span>
      <h2>Entrar no sistema</h2>
      <p>Use as credenciais fornecidas pela administração.</p>
      <form onSubmit={submit} className="form-stack">
        <label>Usuário<input name="username" required autoComplete="username" maxLength={80} placeholder="Seu usuário" /></label>
        <label>Senha<input name="password" type="password" required maxLength={200} autoComplete="current-password" placeholder="Sua senha" /></label>
        {(error || notice) && <p className="error" role="alert"><WifiOff size={16} />{error || notice}</p>}
        <button className="button primary" disabled={pending}>{pending ? 'Entrando…' : 'Entrar'}<ArrowRight size={17} /></button>
      </form>
    </section>
  </main>
}

export function App() {
  const [session, setSession] = useState<Session | null>(null)
  const [tab, setTab] = useState<Tab>('checkout')
  const [notice, setNotice] = useState('')
  const [dark, setDark] = useState(() => localStorage.getItem('refeitorio-theme') === 'dark')
  useEffect(() => {
    document.documentElement.dataset.theme = dark ? 'dark' : 'light'
    localStorage.setItem('refeitorio-theme', dark ? 'dark' : 'light')
  }, [dark])

  const api: Request = useCallback(async <T,>(path: string, options?: RequestInit) => {
    try { return await request<T>(path, options, session?.accessToken) }
    catch (error) {
      if (error instanceof ApiError && error.status === 401) {
        setNotice('Sua sessão expirou. Entre para continuar.')
        setSession(null)
      }
      throw error
    }
  }, [session])

  if (!session) return <Login notice={notice} onLogin={value => { setNotice(''); setTab('checkout'); setSession(value) }} />

  const current = tabs.find(item => item.id === tab)!
  return <div className="shell">
    <a className="skip" href="#content">Ir para o conteúdo</a>
    <aside className="sidebar">
      <Brand />
      <nav aria-label="Menu principal">
        <p className="small-label">Operação</p>
        {tabs.filter(item => allowed(item.id, session.staff.role)).map(item =>
          <button key={item.id} type="button" className={'nav-item ' + (tab === item.id ? 'active' : '')}
            aria-current={tab === item.id ? 'page' : undefined} onClick={() => setTab(item.id)}>
            <item.icon size={18} /><span className="nav-full">{item.label}</span><span className="nav-short">{item.short}</span>
          </button>)}
      </nav>
      <div className="sidebar-bottom">
        <div className="terminal"><span className="terminal-icon"><BadgeCheck size={17} /></span><div>
          <strong>{session.staff.terminalName}</strong><small>{session.staff.unitName}</small>
        </div></div>
      </div>
    </aside>
    <div className="workspace">
      <header className="topbar">
        <div className="breadcrumb"><span>{session.staff.unitName}</span><span aria-hidden="true">/</span><strong>{current.label}</strong></div>
        <div className="top-actions">
          {session.sandbox && <span className="sandbox-chip"><span /> <span>Sandbox</span></span>}
          <button className="icon-button theme" type="button" aria-label={dark ? 'Tema claro' : 'Tema escuro'} onClick={() => setDark(!dark)}>
            {dark ? <Sun size={18} /> : <Moon size={18} />}
          </button>
          <div className="profile"><span className="avatar">{initials(session.staff.name)}</span><div><strong>{session.staff.name}</strong><small>{roles[session.staff.role]}</small></div></div>
          <button className="icon-button" type="button" aria-label="Sair" onClick={() => setSession(null)}><LogOut size={17} /></button>
        </div>
      </header>
      <main className="main" id="content">
        <div hidden={tab !== 'checkout'}><Checkout api={api} timezone={session.staff.timezone} active={tab === 'checkout'} /></div>
        {tab === 'products' && <ProductsPage api={api} />}
        {tab === 'rules' && <RulesPage api={api} />}
        {tab === 'reports' && <ReportsPage api={api} timezone={session.staff.timezone} />}
        {tab === 'integration' && <IntegrationPage api={api} timezone={session.staff.timezone} />}
      </main>
    </div>
  </div>
}

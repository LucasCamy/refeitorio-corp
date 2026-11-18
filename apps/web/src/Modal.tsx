import { useEffect, useId, useRef } from 'react'
import type { ReactNode } from 'react'
import { X } from 'lucide-react'

export function Modal({ title, children, busy, close }: { title: string; children: ReactNode; busy?: boolean; close: () => void }) {
  const dialog = useRef<HTMLDialogElement>(null)
  const titleId = useId()
  useEffect(() => {
    const element = dialog.current
    const trigger = document.activeElement instanceof HTMLElement ? document.activeElement : null
    element?.showModal()
    return () => {
      element?.close()
      if (trigger?.isConnected) trigger.focus({ preventScroll: true })
    }
  }, [])
  return <dialog ref={dialog} aria-labelledby={titleId} onCancel={event => { event.preventDefault(); if (!busy) close() }}>
    <div className="modal-header"><h2 id={titleId}>{title}</h2>
      <button className="icon-button" type="button" aria-label="Fechar" onClick={close} disabled={busy}><X size={19} /></button></div>
    {children}
  </dialog>
}

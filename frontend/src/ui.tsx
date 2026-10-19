import { cloneElement, isValidElement, useEffect, useId, useRef, useState, type DependencyList, type FormEvent, type ReactNode } from 'react';
import { AlertCircle, LoaderCircle, Search, X } from 'lucide-react';
import { ApiError } from './api';

export function useData<T>(load: () => Promise<T>, dependencies: DependencyList = []) {
  const [data, setData] = useState<T | null>(null);
  const [error, setError] = useState<Error | null>(null);
  const [loading, setLoading] = useState(true);
  const [version, setVersion] = useState(0);
  useEffect(() => {
    let active = true; setLoading(true); setError(null);
    load().then(d => { if (active) setData(d); }).catch(e => { if (active) setError(e instanceof Error ? e : new Error(String(e))); })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
    // Callers pass the primitive inputs used by their loader.
  }, [...dependencies, version]);
  return { data, error, loading, reload: () => setVersion(v => v + 1) };
}
export function useDebounced(value: string) {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => { const t = setTimeout(() => setDebounced(value), 250); return () => clearTimeout(t); }, [value]);
  return debounced;
}
export function ErrorBox({ error, retry }: { error: Error; retry?: () => void }) {
  return <div className="error-box" role="alert"><AlertCircle size={20} /><div><strong>{error.message}</strong>
    {error instanceof ApiError && <small>Code : {error.code} · Suivi : {error.correlationId}</small>}
    {retry && <button className="text-button" onClick={retry}>Réessayer</button>}</div></div>;
}
export function Loading() { return <div className="loading" role="status"><LoaderCircle className="spin" size={22} /> Chargement…</div>; }
export function Empty({ children }: { children: ReactNode }) { return <div className="empty">{children}</div>; }
export function Badge({ state }: { state: string }) {
  const labels: Record<string, string> = { Draft: 'Brouillon', InTesting: 'En test', Validated: 'Validé', Passed: 'Réussi', Failed: 'Échoué', Approved: 'Approuvé', Rejected: 'Refusé', Pending: 'À valider' };
  return <span className={`badge ${state.toLowerCase()}`}>{labels[state] || state}</span>;
}
export function SearchBox({ value, onChange, label = 'Rechercher par nom' }: { value: string; onChange: (s: string) => void; label?: string }) {
  return <label className="search"><Search size={18} /><input aria-label={label} placeholder={label} value={value} onChange={e => onChange(e.target.value)} maxLength={120} /></label>;
}
export function Field({ label, children, hint }: { label: string; children: ReactNode; hint?: string }) {
  const id = useId();
  const control = isValidElement<{ id?: string; 'aria-describedby'?: string }>(children)
    ? cloneElement(children, { id, 'aria-describedby': hint ? `${id}-hint` : undefined }) : children;
  return <div className="field"><label htmlFor={id}>{label}</label>{control}{hint && <small id={`${id}-hint`}>{hint}</small>}</div>;
}
export function Modal({ title, children, close, wide = false }: { title: string; children: ReactNode; close: () => void; wide?: boolean }) {
  const ref = useRef<HTMLDialogElement>(null);
  useEffect(() => {
    const previous = document.activeElement as HTMLElement | null;
    const dialog = ref.current; dialog?.showModal();
    return () => { dialog?.close(); previous?.focus(); };
  }, []);
  return <dialog ref={ref} className={`modal ${wide ? 'wide' : ''}`} aria-labelledby="modal-title" onCancel={e => { e.preventDefault(); close(); }}>
    <div className="modal-head"><h2 id="modal-title">{title}</h2><button className="icon-button" aria-label="Fermer" onClick={close}><X size={20} /></button></div>
    {children}
  </dialog>;
}
export function SaveForm({ children, save, close, label = 'Enregistrer' }: { children: ReactNode; save: () => Promise<void>; close: () => void; label?: string }) {
  const [busy, setBusy] = useState(false); const [error, setError] = useState<Error | null>(null);
  async function submit(e: FormEvent) {
    e.preventDefault(); if (busy) return; setBusy(true); setError(null);
    try { await save(); } catch (err) { setError(err instanceof Error ? err : new Error(String(err))); }
    finally { setBusy(false); }
  }
  return <form onSubmit={submit}><fieldset disabled={busy} className="form-fields">{children}</fieldset>{error && <ErrorBox error={error} />}
    <div className="modal-actions"><button type="button" className="button secondary" onClick={close} disabled={busy}>Annuler</button>
      <button className="button" type="submit" disabled={busy}>{busy && <LoaderCircle className="spin" size={17} />}{busy ? 'Traitement…' : label}</button></div></form>;
}
export function ConfirmDelete({ name, perform, close }: { name: string; perform: () => Promise<void>; close: () => void }) {
  return <Modal title="Confirmer la suppression" close={close}><SaveForm close={close} save={perform} label="Supprimer"><p>Supprimer « {name} » ? Les éléments référencés ou disposant d’un historique de tests sont protégés.</p></SaveForm></Modal>;
}
export const dateTime = (value: string) => new Date(value).toLocaleString('fr-FR', { dateStyle: 'short', timeStyle: 'short' });

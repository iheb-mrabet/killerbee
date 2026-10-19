import { useState } from 'react';
import { ArrowRight, Eye, Pencil, Plus, Trash2 } from 'lucide-react';
import { api } from './api';
import type { Characteristic, Ingredient, Model, ModelDetail } from './types';
import { displayScaled, inputScaled, parseScaled } from './scaled.mjs';
import { Badge, ConfirmDelete, Empty, ErrorBox, Field, Loading, Modal, SaveForm, SearchBox, useData, useDebounced } from './ui';

export function ModelsPage({ canEdit, onProcesses }: { canEdit: boolean; onProcesses: (id: string) => void }) {
  const [search, setSearch] = useState(''); const debounced = useDebounced(search);
  const [range, setRange] = useState('');
  const [editor, setEditor] = useState<string | 'new' | null>(null);
  const [detail, setDetail] = useState<string | null>(null);
  const [deleting, setDeleting] = useState<Model | null>(null);
  const r = useData(() => api.call<Model[]>('models.list', { search: debounced, range }), [debounced, range]);
  return <><div className="page-heading"><div><span className="eyebrow">CATALOGUE PRODUITS</span><h1>Modèles de frisbees</h1><p>Les compositions et caractéristiques de vos gammes.</p></div>{canEdit && <button className="button" onClick={() => setEditor('new')}><Plus size={18} />Nouveau modèle</button>}</div>
    <div className="toolbar"><SearchBox value={search} onChange={setSearch} /><label className="inline-field">Gamme<input value={range} onChange={e => setRange(e.target.value)} placeholder="Toutes les gammes" maxLength={80} /></label><button className="button secondary" onClick={r.reload}>Actualiser</button></div>
    {r.error && <ErrorBox error={r.error} retry={r.reload} />}{r.loading && !r.data && <Loading />}
    {r.data && <><div className="results-caption">{r.data.length} modèle{r.data.length !== 1 && 's'} affiché{r.data.length !== 1 && 's'}</div><div className="models-grid">{r.data.map((m, i) => <article className="model-card" key={m.id}>
      <div className={`model-visual visual-${i % 3}`}><span className="model-disc">KB</span><Badge state={m.state} /><span className="model-range">{m.range}</span></div>
      <div className="model-body"><div className="model-title"><h2>{m.name}</h2><span className="version">v{m.revision}</span></div><p>{m.description}</p><div className="model-price"><strong>{displayScaled(m.unitPriceCents)} €</strong><span>prix unitaire HT</span></div>
        <div className="card-actions"><button className="button secondary" onClick={() => setDetail(m.id)}><Eye size={16} />Consulter</button>{canEdit && <><button className="icon-button" aria-label={`Modifier ${m.name}`} onClick={() => setEditor(m.id)}><Pencil size={17} /></button><button className="icon-button danger" aria-label={`Supprimer ${m.name}`} onClick={() => setDeleting(m)}><Trash2 size={17} /></button></>}</div></div>
    </article>)}</div>{r.data.length === 0 && <Empty>Aucun modèle trouvé. {canEdit ? 'Créez votre premier modèle ou modifiez la recherche.' : 'Les modèles validés apparaîtront ici.'}</Empty>}</>}
    {editor && <ModelEditor id={editor === 'new' ? null : editor} close={() => setEditor(null)} saved={() => { setEditor(null); r.reload(); }} />}
    {detail && <ModelViewer id={detail} canEdit={canEdit} close={() => setDetail(null)} edit={() => { setEditor(detail); setDetail(null); }} changed={r.reload} processes={() => onProcesses(detail)} />}
    {deleting && <ConfirmDelete name={deleting.name} close={() => setDeleting(null)} perform={async () => { await api.call('models.delete', { id: deleting.id, revision: deleting.revision }); setDeleting(null); r.reload(); }} />}
  </>;
}

function ModelEditor({ id, close, saved }: { id: string | null; close: () => void; saved: () => void }) {
  const r = useData(async () => {
    const [model, ingredients, definitions] = await Promise.all([id ? api.call<ModelDetail>('models.get', { id }) : Promise.resolve(null), api.call<Ingredient[]>('ingredients.list'), api.call<Characteristic[]>('characteristics.list', { modelId: id })]);
    return { model, ingredients, definitions: definitions.filter(d => !d.modelId || d.modelId === id) };
  }, [id]);
  return <Modal title={id ? 'Modifier le modèle' : 'Nouveau modèle'} close={close} wide>{r.error && <ErrorBox error={r.error} retry={r.reload} />}{r.loading && <Loading />}{r.data && <ModelForm {...r.data} close={close} saved={saved} />}</Modal>;
}

function ModelForm({ model, ingredients, definitions, close, saved }: { model: ModelDetail | null; ingredients: Ingredient[]; definitions: Characteristic[]; close: () => void; saved: () => void }) {
  const [name, setName] = useState(model?.name || ''); const [description, setDescription] = useState(model?.description || '');
  const [price, setPrice] = useState(inputScaled(model?.unitPriceCents || 0)); const [range, setRange] = useState(model?.range || 'Loisir');
  const [rows, setRows] = useState(model?.ingredients.map(i => ({ ingredientId: i.ingredientId, quantity: inputScaled(i.quantityCg) })) || [{ ingredientId: ingredients[0]?.id || '', quantity: '0.01' }]);
  const [values, setValues] = useState<Record<string, string>>(Object.fromEntries(model?.characteristics.map(c => [c.characteristicId, c.value]) || []));
  function row(index: number, changes: Partial<(typeof rows)[number]>) { setRows(rows.map((r, i) => i === index ? { ...r, ...changes } : r)); }
  async function save() {
    await api.call('models.save', { id: model?.id || null, revision: model?.revision || 0, name, description, range, unitPriceCents: parseScaled(price),
      ingredients: rows.map(r => ({ ingredientId: r.ingredientId, quantityCg: parseScaled(r.quantity) })),
      characteristics: Object.fromEntries(Object.entries(values).filter(([, value]) => value !== '').map(([key, value]) => [key, definitions.find(d => d.id === key)?.kind === 'number' ? value.replace(',', '.') : value])) });
    saved();
  }
  return <SaveForm save={save} close={close}><div className="form-grid"><Field label="Nom du modèle *"><input required maxLength={120} value={name} onChange={e => setName(e.target.value)} placeholder="Ex. KB Competition" /></Field><Field label="Gamme *"><input required maxLength={80} value={range} onChange={e => setRange(e.target.value)} /></Field><Field label="Prix unitaire HT (€) *" hint="Deux décimales maximum."><input required inputMode="decimal" value={price} onChange={e => setPrice(e.target.value)} /></Field></div>
    <Field label="Description *"><textarea required maxLength={2000} value={description} onChange={e => setDescription(e.target.value)} rows={3} /></Field>
    <div className="section-heading"><h3>Composition</h3><span>Précision : 0,01 g</span></div>
    {ingredients.length === 0 && <p className="info-box">Ajoutez d’abord un ingrédient depuis la page Ingrédients.</p>}
    {rows.map((r, i) => <div className="composition-row" key={i}><Field label={`Ingrédient ${i + 1} *`}><select required value={r.ingredientId} onChange={e => row(i, { ingredientId: e.target.value })}><option value="">Sélectionner…</option>{ingredients.map(ingredient => <option key={ingredient.id} value={ingredient.id}>{ingredient.name}</option>)}</select></Field><Field label="Quantité (g) *"><input required inputMode="decimal" value={r.quantity} onChange={e => row(i, { quantity: e.target.value })} /></Field><button type="button" className="icon-button danger" aria-label={`Retirer l’ingrédient ${i + 1}`} disabled={rows.length === 1} onClick={() => setRows(rows.filter((_, n) => n !== i))}><Trash2 size={17} /></button></div>)}
    <button type="button" className="text-button" disabled={rows.length >= 100 || ingredients.length === 0} onClick={() => setRows([...rows, { ingredientId: '', quantity: '0.01' }])}><Plus size={16} />Ajouter un ingrédient</button>
    <div className="section-heading"><h3>Caractéristiques</h3><span>Champs configurés par la R&D</span></div>
    <div className="form-grid">{definitions.map(d => <Field key={d.id} label={`${d.name}${d.unit ? ` (${d.unit})` : ''}${d.required ? ' *' : ''}`}>{d.kind === 'boolean' ? <select required={d.required} value={values[d.id] || ''} onChange={e => setValues({ ...values, [d.id]: e.target.value })}><option value="">Non renseigné</option><option value="true">Oui</option><option value="false">Non</option></select> : <input required={d.required} inputMode={d.kind === 'number' ? 'decimal' : 'text'} maxLength={500} value={values[d.id] || ''} onChange={e => setValues({ ...values, [d.id]: e.target.value })} />}</Field>)}</div>{definitions.length === 0 && <p className="muted">Aucune caractéristique définie pour ce modèle.</p>}
    {model && <p className="info-box">L’enregistrement crée une nouvelle version du modèle et remet sa validation à l’état Brouillon.</p>}
  </SaveForm>;
}

function ModelViewer({ id, canEdit, close, edit, changed, processes }: { id: string; canEdit: boolean; close: () => void; edit: () => void; changed: () => void; processes: () => void }) {
  const r = useData(() => api.call<ModelDetail>('models.get', { id }), [id]); const [error, setError] = useState<Error | null>(null); const [busy, setBusy] = useState(false);
  async function submit() { if (!r.data) return; setBusy(true); setError(null); try { await api.call('models.submit', { id, revision: r.data.revision }); r.reload(); changed(); } catch (e) { setError(e as Error); } finally { setBusy(false); } }
  return <Modal title={r.data?.name || 'Détail du modèle'} close={close} wide>{r.error && <ErrorBox error={r.error} retry={r.reload} />}{r.loading && <Loading />}{r.data && <div className="detail-content"><div className="detail-meta"><Badge state={r.data.state} /><span>{r.data.range}</span><span>Version {r.data.revision}</span><strong>{displayScaled(r.data.unitPriceCents)} € HT</strong></div><p>{r.data.description}</p><h3>Composition de fabrication</h3><table><thead><tr><th>INGRÉDIENT</th><th>QUANTITÉ</th></tr></thead><tbody>{r.data.ingredients.map(i => <tr key={i.ingredientId}><td>{i.name}</td><td><strong>{displayScaled(i.quantityCg)} g</strong></td></tr>)}</tbody></table><h3>Caractéristiques du modèle</h3><dl className="characteristics-list">{r.data.characteristics.map(c => <div key={c.characteristicId}><dt>{c.name}</dt><dd>{c.kind === 'boolean' ? c.value === 'true' ? 'Oui' : 'Non' : c.value} {c.unit}</dd></div>)}</dl>{r.data.characteristics.length === 0 && <p className="muted">Aucune valeur renseignée.</p>}{error && <ErrorBox error={error} />}
    <div className="modal-actions wrap"><button className="button secondary" onClick={processes}>Voir les procédés <ArrowRight size={16} /></button>{canEdit && <><button className="button secondary" onClick={edit}><Pencil size={16} />Modifier</button>{r.data.state === 'Draft' && <button className="button" disabled={busy} onClick={submit}>{busy ? 'Soumission…' : 'Soumettre aux tests'}</button>}</>}</div></div>}</Modal>;
}

import { useCallback, useEffect, useMemo, useState } from 'react'
import ReactMarkdown from 'react-markdown'
import {
  createDocument,
  createOpsDocument,
  deleteDocument,
  fetchCategories,
  fetchDocument,
  fetchDocuments,
  updateDocument,
  updateOpsDocument,
} from './api'
import type { RagLibraryCategoryPair, RagLibraryDocument, RagViewMode } from './types'
import './App.css'

const DOC_TYPES = ['document', 'tutorial', 'other'] as const

const PREVIEW_ZOOM_MIN = 50
const PREVIEW_ZOOM_MAX = 200
const PREVIEW_ZOOM_STEP = 10
const PREVIEW_ZOOM_DEFAULT = 100

function viewModeFromFilter(filterContext: string): RagViewMode {
  if (filterContext === 'ops') return 'ops'
  if (filterContext === 'library') return 'library'
  return 'all'
}

function groupCategories(pairs: RagLibraryCategoryPair[], mode: RagViewMode) {
  const m = new Map<string, Set<string>>()
  for (const p of pairs) {
    if (mode === 'ops' && p.context !== 'ops') continue
    if (mode === 'library' && p.context !== 'library') continue
    if (!m.has(p.category)) m.set(p.category, new Set())
    if (mode === 'library') {
      m.get(p.category)!.add(p.subCategory || '')
    }
  }
  return m
}

export default function App() {
  const [pairs, setPairs] = useState<RagLibraryCategoryPair[]>([])
  const [filterContext, setFilterContext] = useState<string>('')
  const [filterCat, setFilterCat] = useState<string>('')
  const [filterSub, setFilterSub] = useState<string>('')
  const [filterType, setFilterType] = useState<string>('')
  const [list, setList] = useState<RagLibraryDocument[]>([])
  const [total, setTotal] = useState(0)
  const [page, setPage] = useState(0)
  const [loading, setLoading] = useState(false)
  const [saving, setSaving] = useState(false)
  const [okMsg, setOkMsg] = useState<string | null>(null)
  const [err, setErr] = useState<string | null>(null)

  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [title, setTitle] = useState('')
  const [category, setCategory] = useState('general')
  const [subCategory, setSubCategory] = useState('')
  const [documentType, setDocumentType] = useState<string>('document')
  const [markdown, setMarkdown] = useState('')
  const [content, setContent] = useState('')
  const [resolution, setResolution] = useState('')
  const [source, setSource] = useState('')
  const [severity, setSeverity] = useState('Error')
  const [documentContext, setDocumentContext] = useState<string>('library')
  const [previewZoom, setPreviewZoom] = useState(PREVIEW_ZOOM_DEFAULT)

  const [libraryDraft, setLibraryDraft] = useState(false)
  const [opsDraft, setOpsDraft] = useState(false)

  const viewMode = viewModeFromFilter(filterContext)
  const showOpsEditor =
    (viewMode === 'ops' && (selectedId != null || opsDraft)) ||
    (viewMode === 'all' && opsDraft) ||
    (viewMode === 'all' && selectedId != null && documentContext === 'ops')
  const showLibraryEditor =
    viewMode === 'library' ||
    (viewMode === 'all' && selectedId != null && documentContext === 'library') ||
    (viewMode === 'all' && libraryDraft)

  const grouped = useMemo(() => groupCategories(pairs, viewMode), [pairs, viewMode])

  const clampPreviewZoom = (value: number) =>
    Math.min(PREVIEW_ZOOM_MAX, Math.max(PREVIEW_ZOOM_MIN, value))

  const onPreviewWheel = (e: React.WheelEvent<HTMLDivElement>) => {
    if (!e.ctrlKey && !e.metaKey) return
    e.preventDefault()
    setPreviewZoom((z) => clampPreviewZoom(z + (e.deltaY < 0 ? PREVIEW_ZOOM_STEP : -PREVIEW_ZOOM_STEP)))
  }

  const reloadCategories = useCallback(() => {
    fetchCategories({ context: filterContext || undefined })
      .then(setPairs)
      .catch((e: Error) => setErr(e.message))
  }, [filterContext])

  const reloadList = useCallback(() => {
    setLoading(true)
    setErr(null)
    fetchDocuments({
      context: filterContext || undefined,
      category: filterCat || undefined,
      subCategory: viewMode !== 'ops' && filterSub ? filterSub : undefined,
      docType: viewMode === 'library' && filterType ? filterType : undefined,
      page,
      pageSize: 30,
    })
      .then((r) => {
        setList(r.items)
        setTotal(r.totalCount)
      })
      .catch((e: Error) => setErr(e.message))
      .finally(() => setLoading(false))
  }, [filterContext, filterCat, filterSub, filterType, page, viewMode])

  useEffect(() => {
    reloadCategories()
  }, [reloadCategories])

  useEffect(() => {
    reloadList()
  }, [reloadList])

  const openNewOps = () => {
    setSelectedId(null)
    setOpsDraft(true)
    setLibraryDraft(false)
    setDocumentContext('ops')
    setTitle('')
    setCategory(filterCat || 'general')
    setContent('')
    setResolution('')
    setSource('manual-ui')
    setSeverity('Error')
    setMarkdown('')
    setErr(null)
    setOkMsg(null)
  }

  const openNew = () => {
    setSelectedId(null)
    setLibraryDraft(true)
    setOpsDraft(false)
    setDocumentContext('library')
    setTitle('')
    setCategory(filterCat || 'general')
    setSubCategory(filterSub || '')
    setDocumentType('document')
    setMarkdown('# Nuovo documento\n\n')
    setContent('')
    setResolution('')
    setSource('')
    setErr(null)
    setOkMsg(null)
  }

  const openDoc = async (id: string) => {
    setErr(null)
    setLibraryDraft(false)
    setOpsDraft(false)
    try {
      const d = await fetchDocument(id)
      setSelectedId(d.id)
      setDocumentContext(d.context || 'ops')
      setTitle(d.title)
      setCategory(d.category || 'general')
      setSubCategory(d.subCategory || '')
      setDocumentType(d.documentType || 'document')
      setMarkdown(d.markdown || '')
      setContent(d.content || '')
      setResolution(d.resolution || '')
      setSource(d.source || '')
      setSeverity(d.severity || 'Error')
    } catch (e) {
      setErr((e as Error).message)
    }
  }

  const saveOps = async () => {
    setErr(null)
    setOkMsg(null)
    if (!content.trim() || !category.trim() || !source.trim()) {
      setErr('Contenuto, categoria e sorgente sono obbligatori.')
      return
    }
    setSaving(true)
    try {
      const payload = {
        content: content.trim(),
        resolution: resolution.trim(),
        source: source.trim(),
        category: category.trim(),
        title: title.trim() || undefined,
        severity: severity || 'Error',
      }
      if (selectedId) {
        await updateOpsDocument(selectedId, payload)
        setOkMsg('Documento operativo aggiornato.')
      } else {
        const id = await createOpsDocument(payload)
        if (!id) {
          throw new Error('Il server non ha restituito l\'id del documento creato.')
        }
        setSelectedId(id)
        setOpsDraft(false)
        setDocumentContext('ops')
        setFilterContext('ops')
        setFilterCat('')
        setPage(0)
        setOkMsg('Documento operativo creato.')
      }
      reloadList()
      reloadCategories()
    } catch (e) {
      const msg = e instanceof Error ? e.message : String(e)
      setErr(msg || 'Salvataggio non riuscito.')
    } finally {
      setSaving(false)
    }
  }

  const saveLibrary = async () => {
    setErr(null)
    setOkMsg(null)
    if (!title.trim() || !category.trim()) {
      setErr('Titolo e categoria sono obbligatori.')
      return
    }
    setSaving(true)
    try {
      if (selectedId) {
        await updateDocument(selectedId, {
          title: title.trim(),
          markdown,
          category: category.trim(),
          subCategory: subCategory.trim() || undefined,
          documentType: documentType || undefined,
        })
        setOkMsg('Documento aggiornato.')
      } else {
        const id = await createDocument({
          title: title.trim(),
          markdown,
          category: category.trim(),
          subCategory: subCategory.trim() || undefined,
          documentType: documentType || undefined,
        })
        if (!id) {
          throw new Error('Il server non ha restituito l\'id del documento creato.')
        }
        setSelectedId(id)
        setLibraryDraft(false)
        setDocumentContext('library')
        setFilterContext('library')
        setFilterCat('')
        setFilterSub('')
        setFilterType('')
        setPage(0)
        setOkMsg('Documento creato.')
      }
      reloadList()
      reloadCategories()
    } catch (e) {
      const msg = e instanceof Error ? e.message : String(e)
      setErr(msg || 'Salvataggio non riuscito.')
    } finally {
      setSaving(false)
    }
  }

  const remove = async () => {
    if (!selectedId) return
    if (!window.confirm('Eliminare questo documento dalla knowledge base?')) return
    setErr(null)
    try {
      await deleteDocument(selectedId)
      if (documentContext === 'ops') {
        openNewOps()
        setOpsDraft(false)
        setSelectedId(null)
      } else {
        openNew()
      }
      reloadList()
      reloadCategories()
    } catch (e) {
      setErr((e as Error).message)
    }
  }

  const subsForFilterCat = filterCat ? [...(grouped.get(filterCat) ?? [])].sort() : []

  const headerSubtitle =
    viewMode === 'ops'
      ? 'Knowledge operativa: contenuto, risoluzione e sorgente (embedding su content)'
      : viewMode === 'library'
        ? 'Documenti markdown per la libreria RAG'
        : 'Tutti i contesti — seleziona un filtro per campi dedicati'

  return (
    <div className="layout">
      <header className="top">
        <h1>Libreria RAG</h1>
        <p className="muted">{headerSubtitle}</p>
      </header>

      {err && <div className="banner error">{err}</div>}
      {okMsg && !err && <div className="banner success">{okMsg}</div>}

      <div className="grid">
        <aside className="panel">
          <h2>Filtri</h2>
          <label>
            Contesto
            <select
              value={filterContext}
              onChange={(e) => {
                const next = e.target.value
                setFilterContext(next)
                setFilterCat('')
                setFilterSub('')
                setFilterType('')
                setPage(0)
                setSelectedId(null)
                setLibraryDraft(false)
                setOpsDraft(false)
                setErr(null)
                setOkMsg(null)
              }}
            >
              <option value="">Tutti (ops + libreria)</option>
              <option value="ops">FlowScheduler / strumenti (ops)</option>
              <option value="library">Libreria markdown (library)</option>
            </select>
          </label>
          <label>
            Categoria
            <select
              value={filterCat}
              onChange={(e) => {
                setFilterCat(e.target.value)
                setFilterSub('')
                setPage(0)
              }}
            >
              <option value="">(tutte)</option>
              {[...grouped.keys()].sort().map((c) => (
                <option key={c} value={c}>
                  {c}
                </option>
              ))}
            </select>
          </label>
          {viewMode === 'library' && (
            <>
              <label>
                Sottocategoria
                <select
                  value={filterSub}
                  onChange={(e) => {
                    setFilterSub(e.target.value)
                    setPage(0)
                  }}
                  disabled={!filterCat}
                >
                  <option value="">(tutte)</option>
                  {subsForFilterCat.map((s) => (
                    <option key={s || '__empty__'} value={s}>
                      {s || '(nessuna)'}
                    </option>
                  ))}
                </select>
              </label>
              <label>
                Tipo documento
                <select
                  value={filterType}
                  onChange={(e) => {
                    setFilterType(e.target.value)
                    setPage(0)
                  }}
                >
                  <option value="">(tutti)</option>
                  {DOC_TYPES.map((t) => (
                    <option key={t} value={t}>
                      {t}
                    </option>
                  ))}
                </select>
              </label>
            </>
          )}
          <button type="button" className="secondary" onClick={() => reloadList()} disabled={loading}>
            Aggiorna elenco
          </button>
          {viewMode === 'ops' && (
            <button type="button" onClick={openNewOps}>
              Nuovo documento ops
            </button>
          )}
          {(viewMode === 'library' || viewMode === 'all') && (
            <button type="button" onClick={openNew}>
              Nuovo documento library
            </button>
          )}
        </aside>

        <section className="panel list">
          <div className="list-head">
            <h2>Documenti ({total})</h2>
            {loading && <span className="muted">Caricamento…</span>}
          </div>
          <ul className="doc-list">
            {list.map((d) => (
              <li key={d.id}>
                <button
                  type="button"
                  className={d.id === selectedId ? 'doc-item active' : 'doc-item'}
                  onClick={() => void openDoc(d.id)}
                >
                  <span className="doc-title">{d.title}</span>
                  <span className="muted small">
                    <span className="ctx-badge">{d.context}</span>{' '}
                    {d.context === 'ops' && d.severity && d.severity !== 'Error' && (
                      <span className={`severity-badge severity-${d.severity.toLowerCase()}`}>{d.severity}</span>
                    )}{' '}
                    {d.context === 'ops' ? (
                      <>
                        {d.category}
                        {d.source ? ` · ${d.source}` : ''}
                      </>
                    ) : (
                      <>
                        {d.category}
                        {d.subCategory ? ` / ${d.subCategory}` : ''} · {d.documentType}
                      </>
                    )}
                  </span>
                  <span className="muted small doc-id">ID: {d.id}</span>
                </button>
              </li>
            ))}
          </ul>
          <div className="pager">
            <button type="button" disabled={page <= 0} onClick={() => setPage((p) => p - 1)}>
              Precedente
            </button>
            <span className="muted">
              Pagina {page + 1} / {Math.max(1, Math.ceil(total / 30))}
            </span>
            <button
              type="button"
              disabled={(page + 1) * 30 >= total}
              onClick={() => setPage((p) => p + 1)}
            >
              Successiva
            </button>
          </div>
        </section>

        <section className="panel editor">
          <h2>
            {selectedId
              ? showOpsEditor
                ? 'Modifica operativo'
                : showLibraryEditor
                  ? 'Modifica'
                  : 'Editor'
              : showOpsEditor
                ? 'Nuovo operativo'
                : showLibraryEditor
                  ? 'Nuovo'
                  : 'Editor'}
          </h2>

          {!showOpsEditor && !showLibraryEditor && viewMode === 'ops' && (
            <p className="muted">
              Seleziona un documento dall&apos;elenco o crea un nuovo documento ops.
            </p>
          )}

          {(selectedId || opsDraft || libraryDraft) && (
            <p className="muted small">
              Contesto Redis: <strong>{documentContext}</strong>
            </p>
          )}

          {showOpsEditor && (
            <div className="ops-detail">
              <div className="form-row two">
                <label>
                  Doc ID
                  <input value={selectedId || '(nuovo)'} readOnly disabled />
                </label>
                <label>
                  Severity
                  <select value={severity} onChange={(e) => setSeverity(e.target.value)}>
                    <option value="Information">Information</option>
                    <option value="Warning">Warning</option>
                    <option value="Error">Error</option>
                    <option value="Fatal">Fatal</option>
                  </select>
                </label>
              </div>
              <div className="form-row two">
                <label>
                  Titolo (opzionale)
                  <input value={title} onChange={(e) => setTitle(e.target.value)} />
                </label>
                <label>
                  Sorgente
                  <input value={source} onChange={(e) => setSource(e.target.value)} />
                </label>
              </div>
              <div className="form-row">
                <label>
                  Categoria
                  <input value={category} onChange={(e) => setCategory(e.target.value)} />
                </label>
              </div>
              <label>
                Contenuto (errore / contesto) — usato per l&apos;embedding
                <textarea
                  value={content}
                  onChange={(e) => setContent(e.target.value)}
                  rows={8}
                  spellCheck={false}
                />
              </label>
              <label>
                Risoluzione
                <textarea
                  value={resolution}
                  onChange={(e) => setResolution(e.target.value)}
                  rows={10}
                  spellCheck={false}
                />
              </label>
              <div className="actions">
                <button type="button" onClick={() => void saveOps()} disabled={saving}>
                  {saving ? 'Salvataggio…' : 'Salva'}
                </button>
                {selectedId && (
                  <button type="button" className="danger" onClick={() => void remove()}>
                    Elimina
                  </button>
                )}
              </div>
            </div>
          )}

          {showLibraryEditor && (
            <>
              <div className="form-row">
                <label>
                  Titolo
                  <input value={title} onChange={(e) => setTitle(e.target.value)} />
                </label>
              </div>
              <div className="form-row two">
                <label>
                  Categoria
                  <input value={category} onChange={(e) => setCategory(e.target.value)} />
                </label>
                <label>
                  Sottocategoria
                  <input
                    value={subCategory}
                    onChange={(e) => setSubCategory(e.target.value)}
                    placeholder="opzionale"
                  />
                </label>
              </div>
              <label>
                Tipo documento
                <select value={documentType} onChange={(e) => setDocumentType(e.target.value)}>
                  {DOC_TYPES.map((t) => (
                    <option key={t} value={t}>
                      {t}
                    </option>
                  ))}
                </select>
              </label>
              <div className="split">
                <div className="split-pane">
                  <h3>Markdown</h3>
                  <textarea
                    value={markdown}
                    onChange={(e) => setMarkdown(e.target.value)}
                    spellCheck={false}
                  />
                </div>
                <div className="split-pane preview">
                  <div className="preview-head">
                    <h3>Anteprima</h3>
                    <div className="preview-zoom" role="group" aria-label="Zoom anteprima">
                      <button
                        type="button"
                        className="secondary zoom-btn"
                        title="Riduci zoom"
                        onClick={() =>
                          setPreviewZoom((z) => clampPreviewZoom(z - PREVIEW_ZOOM_STEP))
                        }
                      >
                        −
                      </button>
                      <span className="zoom-label">{previewZoom}%</span>
                      <button
                        type="button"
                        className="secondary zoom-btn"
                        title="Aumenta zoom"
                        onClick={() =>
                          setPreviewZoom((z) => clampPreviewZoom(z + PREVIEW_ZOOM_STEP))
                        }
                      >
                        +
                      </button>
                      <button
                        type="button"
                        className="secondary zoom-btn"
                        title="Ripristina zoom 100%"
                        onClick={() => setPreviewZoom(PREVIEW_ZOOM_DEFAULT)}
                      >
                        100%
                      </button>
                    </div>
                  </div>
                  <div
                    className="preview-viewport"
                    onWheel={onPreviewWheel}
                    title="Ctrl + rotella del mouse per zoomare"
                  >
                    <div
                      className="markdown-body"
                      style={{ ['--preview-scale' as string]: String(previewZoom / 100) }}
                    >
                      <ReactMarkdown>{markdown}</ReactMarkdown>
                    </div>
                  </div>
                </div>
              </div>
              <div className="actions">
                <button type="button" onClick={() => void saveLibrary()} disabled={saving}>
                  {saving ? 'Salvataggio…' : 'Salva'}
                </button>
                {selectedId && (
                  <button type="button" className="danger" onClick={() => void remove()}>
                    Elimina
                  </button>
                )}
              </div>
            </>
          )}

          {viewMode === 'all' && !selectedId && !libraryDraft && !opsDraft && (
            <p className="muted">
              Seleziona un documento dall&apos;elenco oppure filtra per <strong>ops</strong> o{' '}
              <strong>library</strong> per vedere i campi dedicati.
            </p>
          )}
        </section>
      </div>
    </div>
  )
}

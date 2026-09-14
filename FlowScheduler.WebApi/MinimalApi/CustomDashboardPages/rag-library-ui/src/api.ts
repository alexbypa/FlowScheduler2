import type { RagLibraryCategoryPair, RagLibraryDocument, RagLibraryListResponse } from './types'

const jsonHeaders = { Accept: 'application/json', 'Content-Type': 'application/json' }

function qs(params: Record<string, string | number | undefined>) {
  const u = new URLSearchParams()
  for (const [k, v] of Object.entries(params)) {
    if (v === undefined || v === '') continue
    u.set(k, String(v))
  }
  const s = u.toString()
  return s ? `?${s}` : ''
}

async function readApiErrorFromText(res: Response, rawText: string): Promise<string> {
  const text = rawText.trim()
  if (text) {
    try {
      const j = JSON.parse(text) as {
        detail?: string
        title?: string
        message?: string
      }
      if (j.detail) return j.detail
      if (j.message) return j.message
      if (j.title) return j.title
    } catch {
      return text.length > 500 ? `${text.slice(0, 500)}…` : text
    }
  }
  return `Richiesta fallita (${res.status} ${res.statusText})`
}

async function readApiError(res: Response): Promise<string> {
  return readApiErrorFromText(res, await res.text())
}

export async function fetchDocuments(args: {
  context?: string
  category?: string
  subCategory?: string
  docType?: string
  page?: number
  pageSize?: number
}): Promise<RagLibraryListResponse> {
  const q = qs({
    context: args.context,
    category: args.category,
    subCategory: args.subCategory,
    docType: args.docType,
    page: args.page ?? 0,
    pageSize: args.pageSize ?? 50,
  })
  const res = await fetch(`/rag/library/documents${q}`, { headers: { Accept: 'application/json' } })
  if (!res.ok) throw new Error(await readApiError(res))
  return res.json()
}

export async function fetchDocument(id: string): Promise<RagLibraryDocument> {
  const res = await fetch(`/rag/library/documents/${encodeURIComponent(id)}`, {
    headers: { Accept: 'application/json' },
  })
  if (!res.ok) throw new Error(await readApiError(res))
  return res.json()
}

function parseSavedDocumentId(res: Response, bodyText: string): string {
  const trimmed = bodyText.trim()
  if (trimmed) {
    try {
      const j = JSON.parse(trimmed) as { id?: string; Id?: string }
      const fromJson = j.id ?? j.Id
      if (fromJson) return fromJson
    } catch {
      throw new Error(`Risposta non valida dal server: ${trimmed.slice(0, 200)}`)
    }
  }

  const loc = res.headers.get('Location')
  if (loc) {
    const parts = loc.split('/').filter(Boolean)
    const fromLoc = parts[parts.length - 1]
    if (fromLoc) return fromLoc
  }

  if (res.status === 201 || res.status === 200) {
    throw new Error(
      `Salvataggio accettato (${res.status}) ma il server non ha restituito l'id del documento.`,
    )
  }

  return ''
}

export async function createDocument(body: {
  title: string
  markdown: string
  category: string
  subCategory?: string
  documentType?: string
}): Promise<string> {
  const res = await fetch('/rag/library/documents', {
    method: 'POST',
    headers: jsonHeaders,
    body: JSON.stringify({
      title: body.title,
      markdown: body.markdown,
      category: body.category,
      subCategory: body.subCategory || null,
      documentType: body.documentType || null,
    }),
  })
  const text = await res.text()
  if (!res.ok) throw new Error(await readApiErrorFromText(res, text))
  return parseSavedDocumentId(res, text)
}

export async function updateDocument(
  id: string,
  body: {
    title: string
    markdown: string
    category: string
    subCategory?: string
    documentType?: string
  },
): Promise<void> {
  const res = await fetch(`/rag/library/documents/${encodeURIComponent(id)}`, {
    method: 'PUT',
    headers: jsonHeaders,
    body: JSON.stringify({
      title: body.title,
      markdown: body.markdown,
      category: body.category,
      subCategory: body.subCategory || null,
      documentType: body.documentType || null,
    }),
  })
  const text = await res.text()
  if (!res.ok) throw new Error(await readApiErrorFromText(res, text))
  parseSavedDocumentId(res, text)
}

export async function createOpsDocument(body: {
  content: string
  resolution: string
  source: string
  category: string
  title?: string
  severity?: string
}): Promise<string> {
  const res = await fetch('/rag/library/documents/ops', {
    method: 'POST',
    headers: jsonHeaders,
    body: JSON.stringify({
      content: body.content,
      resolution: body.resolution,
      source: body.source,
      category: body.category,
      title: body.title || null,
      severity: body.severity || null,
    }),
  })
  const text = await res.text()
  if (!res.ok) throw new Error(await readApiErrorFromText(res, text))
  return parseSavedDocumentId(res, text)
}

export async function updateOpsDocument(
  id: string,
  body: {
    content: string
    resolution: string
    source: string
    category: string
    title?: string
    severity?: string
  },
): Promise<void> {
  const res = await fetch(`/rag/library/documents/${encodeURIComponent(id)}/ops`, {
    method: 'PUT',
    headers: jsonHeaders,
    body: JSON.stringify({
      content: body.content,
      resolution: body.resolution,
      source: body.source,
      category: body.category,
      title: body.title || null,
      severity: body.severity || null,
    }),
  })
  const text = await res.text()
  if (!res.ok) throw new Error(await readApiErrorFromText(res, text))
  parseSavedDocumentId(res, text)
}

export async function deleteDocument(id: string): Promise<void> {
  const res = await fetch(`/rag/documents/${encodeURIComponent(id)}`, { method: 'DELETE' })
  if (!res.ok && res.status !== 404) throw new Error(await readApiError(res))
}

export async function fetchCategories(args?: { context?: string; maxDocs?: number }): Promise<RagLibraryCategoryPair[]> {
  const q = qs({
    context: args?.context,
    maxDocs: args?.maxDocs ?? 2000,
  })
  const res = await fetch(`/rag/library/categories${q}`, {
    headers: { Accept: 'application/json' },
  })
  if (!res.ok) throw new Error(await readApiError(res))
  return res.json()
}

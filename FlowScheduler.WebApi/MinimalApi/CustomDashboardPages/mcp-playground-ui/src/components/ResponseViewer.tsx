import { useState } from 'react'
import type { ToolResult } from '../types'

interface ResponseViewerProps {
  result: ToolResult | null
  error: string | null
  elapsed: number | null
  loading: boolean
}

export default function ResponseViewer({ result, error, elapsed, loading }: ResponseViewerProps) {
  const [viewMode, setViewMode] = useState<'formatted' | 'raw'>('formatted')

  if (loading) {
    return (
      <div className="response-viewer response-viewer--loading">
        <span className="response-viewer__spinner">Executing...</span>
      </div>
    )
  }

  if (error) {
    return (
      <div className="response-viewer response-viewer--error">
        <div className="response-viewer__header">
          <span>Error</span>
          {elapsed !== null && <span className="response-viewer__elapsed">{elapsed}ms</span>}
        </div>
        <pre className="response-viewer__content">{error}</pre>
      </div>
    )
  }

  if (!result) return null

  const text = result.content
    .filter(c => c.type === 'text' && c.text)
    .map(c => c.text)
    .join('\n')

  return (
    <div className="response-viewer">
      <div className="response-viewer__header">
        <span>Response</span>
        <div className="response-viewer__controls">
          {elapsed !== null && <span className="response-viewer__elapsed">{elapsed}ms</span>}
          <button
            className={`response-viewer__mode ${viewMode === 'formatted' ? 'response-viewer__mode--active' : ''}`}
            onClick={() => setViewMode('formatted')}
          >
            Formatted
          </button>
          <button
            className={`response-viewer__mode ${viewMode === 'raw' ? 'response-viewer__mode--active' : ''}`}
            onClick={() => setViewMode('raw')}
          >
            Raw JSON
          </button>
        </div>
      </div>
      <pre className="response-viewer__content">
        {viewMode === 'formatted' ? text : JSON.stringify(result, null, 2)}
      </pre>
    </div>
  )
}

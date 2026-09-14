import { useState, useCallback } from 'react'
import type { ToolDefinition, ToolResult, SchemaProperty } from '../types'
import { callTool } from '../mcp-client'
import ResponseViewer from './ResponseViewer'

interface ToolPanelProps {
  tool: ToolDefinition
}

function buildDefaultValues(tool: ToolDefinition): Record<string, string> {
  const values: Record<string, string> = {}
  const props = tool.inputSchema?.properties
  if (!props) return values
  for (const [key, schema] of Object.entries(props)) {
    if (schema.default !== undefined && schema.default !== null) {
      values[key] = String(schema.default)
    } else {
      values[key] = ''
    }
  }
  return values
}

function coerceValue(value: string, schema: SchemaProperty): unknown {
  if (value === '' && (schema.nullable || schema.default === undefined)) return undefined
  if (schema.type === 'integer' || schema.type === 'number') {
    const n = Number(value)
    return isNaN(n) ? undefined : n
  }
  return value
}

export default function ToolPanel({ tool }: ToolPanelProps) {
  const [values, setValues] = useState<Record<string, string>>(() => buildDefaultValues(tool))
  const [result, setResult] = useState<ToolResult | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [elapsed, setElapsed] = useState<number | null>(null)
  const [loading, setLoading] = useState(false)

  const properties = tool.inputSchema?.properties ?? {}
  const required = new Set(tool.inputSchema?.required ?? [])

  const handleChange = useCallback((key: string, value: string) => {
    setValues(prev => ({ ...prev, [key]: value }))
  }, [])

  const handleExecute = useCallback(async () => {
    setLoading(true)
    setError(null)
    setResult(null)
    setElapsed(null)

    const args: Record<string, unknown> = {}
    for (const [key, schema] of Object.entries(properties)) {
      const coerced = coerceValue(values[key] ?? '', schema)
      if (coerced !== undefined) {
        args[key] = coerced
      }
    }

    const start = performance.now()
    try {
      const res = await callTool(tool.name, args)
      setElapsed(Math.round(performance.now() - start))
      setResult(res)
    } catch (e) {
      setElapsed(Math.round(performance.now() - start))
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setLoading(false)
    }
  }, [tool.name, values, properties])

  return (
    <div className="tool-panel">
      <div className="tool-panel__header">
        <h2 className="tool-panel__name">{tool.name}</h2>
        {tool.description && <p className="tool-panel__desc">{tool.description}</p>}
      </div>

      <div className="tool-panel__params">
        <div className="tool-panel__params-title">Parameters</div>
        {Object.entries(properties).map(([key, schema]) => (
          <div className="tool-panel__field" key={key}>
            <label className="tool-panel__label">
              {key}
              {required.has(key) && <span className="tool-panel__required">*</span>}
            </label>
            <input
              type={schema.type === 'integer' || schema.type === 'number' ? 'number' : 'text'}
              value={values[key] ?? ''}
              onChange={e => handleChange(key, e.target.value)}
              placeholder={schema.description ?? (required.has(key) ? 'required' : 'optional')}
            />
            {schema.description && (
              <span className="tool-panel__hint">{schema.description}</span>
            )}
          </div>
        ))}
      </div>

      <button
        className="tool-panel__execute"
        onClick={handleExecute}
        disabled={loading}
      >
        {loading ? 'Executing...' : '\u25b6 Execute'}
      </button>

      <ResponseViewer result={result} error={error} elapsed={elapsed} loading={loading} />
    </div>
  )
}

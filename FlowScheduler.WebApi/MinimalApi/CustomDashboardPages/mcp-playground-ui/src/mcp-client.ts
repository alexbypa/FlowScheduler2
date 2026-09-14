import type { ToolDefinition, ToolResult, JsonRpcResponse } from './types'

let sessionId: string | null = null
let nextId = 1

function getNextId(): number {
  return nextId++
}

async function mcpRequest<T>(method: string, params?: Record<string, unknown>): Promise<T> {
  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
    'Accept': 'application/json, text/event-stream',
  }
  if (sessionId) {
    headers['Mcp-Session-Id'] = sessionId
  }

  const body = JSON.stringify({
    jsonrpc: '2.0',
    method,
    ...(params !== undefined ? { params } : {}),
    id: getNextId(),
  })

  const controller = new AbortController()
  const timeout = setTimeout(() => controller.abort(), 10000)

  let res: Response
  try {
    res = await fetch('/mcp', {
      method: 'POST',
      headers,
      body,
      signal: controller.signal,
    })
  } finally {
    clearTimeout(timeout)
  }

  if (!res.ok) {
    const text = await res.text()
    throw new Error(`MCP request failed (${res.status}): ${text.slice(0, 300)}`)
  }

  // Handle SSE responses: extract first JSON-RPC message from event stream
  const contentType = res.headers.get('Content-Type') ?? ''
  let parsed: JsonRpcResponse<T>

  if (contentType.includes('text/event-stream')) {
    const text = await res.text()
    const dataLine = text.split('\n').find(line => line.startsWith('data: '))
    if (!dataLine) {
      throw new Error('No data in SSE response')
    }
    parsed = JSON.parse(dataLine.slice(6)) as JsonRpcResponse<T>
  } else {
    parsed = (await res.json()) as JsonRpcResponse<T>
  }

  // Capture session ID from response headers
  const sid = res.headers.get('Mcp-Session-Id')
  if (sid) {
    sessionId = sid
  }

  if (parsed.error) {
    throw new Error(`MCP error ${parsed.error.code}: ${parsed.error.message}`)
  }

  return parsed.result as T
}

export async function initialize(): Promise<void> {
  sessionId = null
  nextId = 1
  await mcpRequest('initialize', {
    protocolVersion: '2025-03-26',
    capabilities: {},
    clientInfo: { name: 'mcp-playground', version: '1.0.0' },
  })
  // Send initialized notification (no id = notification)
  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
    'Accept': 'application/json, text/event-stream',
  }
  if (sessionId) {
    headers['Mcp-Session-Id'] = sessionId
  }
  await fetch('/mcp', {
    method: 'POST',
    headers,
    body: JSON.stringify({ jsonrpc: '2.0', method: 'notifications/initialized' }),
  })
}

export async function listTools(): Promise<ToolDefinition[]> {
  const result = await mcpRequest<{ tools: ToolDefinition[] }>('tools/list')
  return result.tools
}

export async function callTool(name: string, args: Record<string, unknown>): Promise<ToolResult> {
  return mcpRequest<ToolResult>('tools/call', { name, arguments: args })
}

export function isConnected(): boolean {
  return sessionId !== null
}

export function resetSession(): void {
  sessionId = null
  nextId = 1
}

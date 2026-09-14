export interface ToolDefinition {
  name: string
  description?: string
  inputSchema?: {
    type: string
    properties?: Record<string, SchemaProperty>
    required?: string[]
  }
}

export interface SchemaProperty {
  type: string
  description?: string
  default?: unknown
  nullable?: boolean
}

export interface ToolCallContent {
  type: string
  text?: string
}

export interface ToolResult {
  content: ToolCallContent[]
}

export interface JsonRpcResponse<T = unknown> {
  jsonrpc: string
  id: number
  result?: T
  error?: { code: number; message: string; data?: unknown }
}

export type ConnectionStatus = 'disconnected' | 'connecting' | 'connected' | 'error'

import { useEffect, useState, useCallback } from 'react'
import type { ToolDefinition, ConnectionStatus } from './types'
import { initialize, listTools, resetSession } from './mcp-client'
import StatusBar from './components/StatusBar'
import ToolSelector from './components/ToolSelector'
import ToolPanel from './components/ToolPanel'

export default function App() {
  const [status, setStatus] = useState<ConnectionStatus>('disconnected')
  const [tools, setTools] = useState<ToolDefinition[]>([])
  const [activeTool, setActiveTool] = useState<string | null>(null)

  const connect = useCallback(async () => {
    setStatus('connecting')
    try {
      await initialize()
      const toolList = await listTools()
      setTools(toolList)
      setActiveTool(toolList.length > 0 ? toolList[0].name : null)
      setStatus('connected')
    } catch (e) {
      console.error('MCP connection failed:', e)
      resetSession()
      setStatus('error')
    }
  }, [])

  useEffect(() => {
    connect()
  }, [connect])

  const selectedTool = tools.find(t => t.name === activeTool) ?? null

  return (
    <div className="app">
      <StatusBar status={status} toolCount={tools.length} onRetry={connect} />
      {status === 'connected' && (
        <>
          <ToolSelector tools={tools} activeTool={activeTool} onSelect={setActiveTool} />
          {selectedTool && <ToolPanel key={selectedTool.name} tool={selectedTool} />}
        </>
      )}
      {status === 'error' && (
        <div className="app__error">
          Failed to connect to MCP server at <code>/mcp</code>. Is the WebApi running?
        </div>
      )}
    </div>
  )
}

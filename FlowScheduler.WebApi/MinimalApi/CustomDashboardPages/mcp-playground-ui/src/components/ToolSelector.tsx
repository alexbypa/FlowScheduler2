import type { ToolDefinition } from '../types'

interface ToolSelectorProps {
  tools: ToolDefinition[]
  activeTool: string | null
  onSelect: (name: string) => void
}

export default function ToolSelector({ tools, activeTool, onSelect }: ToolSelectorProps) {
  return (
    <nav className="tool-selector">
      {tools.map(tool => (
        <button
          key={tool.name}
          className={`tool-selector__tab ${tool.name === activeTool ? 'tool-selector__tab--active' : ''}`}
          onClick={() => onSelect(tool.name)}
          title={tool.description}
        >
          {tool.name}
        </button>
      ))}
    </nav>
  )
}

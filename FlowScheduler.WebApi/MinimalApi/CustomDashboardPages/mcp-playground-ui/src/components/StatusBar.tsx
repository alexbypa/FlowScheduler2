import type { ConnectionStatus } from '../types'

interface StatusBarProps {
  status: ConnectionStatus
  toolCount: number
  onRetry: () => void
}

const statusConfig: Record<ConnectionStatus, { icon: string; label: string; className: string }> = {
  disconnected: { icon: '\u26aa', label: 'Disconnected', className: 'status--disconnected' },
  connecting:   { icon: '\ud83d\udfe1', label: 'Connecting...', className: 'status--connecting' },
  connected:    { icon: '\ud83d\udfe2', label: 'Connected', className: 'status--connected' },
  error:        { icon: '\ud83d\udd34', label: 'Disconnected', className: 'status--error' },
}

export default function StatusBar({ status, toolCount, onRetry }: StatusBarProps) {
  const cfg = statusConfig[status]
  return (
    <header className="status-bar">
      <div className="status-bar__title">MCP Playground</div>
      <div className={`status-bar__status ${cfg.className}`}>
        <span>{cfg.icon}</span>
        <span>{cfg.label}</span>
        {status === 'connected' && <span className="status-bar__count">{toolCount} tools</span>}
        {status === 'error' && (
          <button className="status-bar__retry" onClick={onRetry}>Retry</button>
        )}
      </div>
    </header>
  )
}

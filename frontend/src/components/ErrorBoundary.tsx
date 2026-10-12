import { Component, type ErrorInfo, type ReactNode } from 'react'

interface ErrorBoundaryProps {
  fallback: ReactNode
  children: ReactNode
}

/**
 * Contains a render failure to one part of the page. Without it, an error in the chart (or a
 * failed lazy chunk download after a deploy) would unmount the whole app.
 */
export class ErrorBoundary extends Component<ErrorBoundaryProps, { hasError: boolean }> {
  override state = { hasError: false }

  static getDerivedStateFromError(): { hasError: boolean } {
    return { hasError: true }
  }

  override componentDidCatch(error: Error, info: ErrorInfo): void {
    console.error('Render error contained by ErrorBoundary', error, info.componentStack)
  }

  override render(): ReactNode {
    return this.state.hasError ? this.props.fallback : this.props.children
  }
}

import { render, screen } from '@testing-library/react'
import { ErrorBoundary } from './ErrorBoundary'

function Boom(): never {
  throw new Error('render failed')
}

describe('ErrorBoundary', () => {
  it('renders children when nothing fails', () => {
    render(
      <ErrorBoundary fallback={<p>fallback</p>}>
        <p>content</p>
      </ErrorBoundary>,
    )

    expect(screen.getByText('content')).toBeInTheDocument()
  })

  it('renders the fallback instead of crashing the tree', () => {
    vi.spyOn(console, 'error').mockImplementation(() => {})

    render(
      <div>
        <p>sibling</p>
        <ErrorBoundary fallback={<p>fallback</p>}>
          <Boom />
        </ErrorBoundary>
      </div>,
    )

    expect(screen.getByText('fallback')).toBeInTheDocument()
    expect(screen.getByText('sibling')).toBeInTheDocument()
  })
})

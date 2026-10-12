import { render, screen } from '@testing-library/react'
import { cloneElement, type ReactElement } from 'react'
import type * as Recharts from 'recharts'
import { TSLA_DAYS } from '../../test/server'
import { DailySummaryChart } from './DailySummaryChart'

// jsdom has no layout, so give ResponsiveContainer's child a fixed size to make Recharts render.
vi.mock('recharts', async (importOriginal) => {
  const recharts = await importOriginal<typeof Recharts>()
  return {
    ...recharts,
    ResponsiveContainer: ({
      children,
    }: {
      children: ReactElement<{ width?: number; height?: number }>
    }) => cloneElement(children, { width: 800, height: 300 }),
  }
})

describe('DailySummaryChart', () => {
  it('renders the price and volume charts with accessible titles', () => {
    const { container } = render(<DailySummaryChart symbol="TSLA" data={TSLA_DAYS} />)

    expect(screen.getByText('Daily price range')).toBeInTheDocument()
    expect(screen.getByText('Volume')).toBeInTheDocument()
    const titles = [...container.querySelectorAll('svg title')].map((t) => t.textContent)
    expect(titles).toEqual(['Daily low and high average prices for TSLA', 'Daily volume for TSLA'])
  })

  it('keys both price series in a legend', () => {
    render(<DailySummaryChart symbol="TSLA" data={TSLA_DAYS} />)

    expect(screen.getByText('High average')).toBeInTheDocument()
    expect(screen.getByText('Low average')).toBeInTheDocument()
  })

  it('draws one volume column per day', () => {
    const { container } = render(<DailySummaryChart symbol="TSLA" data={TSLA_DAYS} />)

    expect(container.querySelectorAll('.recharts-bar-rectangle')).toHaveLength(TSLA_DAYS.length)
  })
})

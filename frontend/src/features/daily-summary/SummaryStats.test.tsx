import { render, screen } from '@testing-library/react'
import { SummaryStats } from './SummaryStats'

const btc = [
  { day: '2026-09-16', lowAverage: 75675.8049, highAverage: 76001.5, volume: 18_000_000_000 },
  { day: '2026-09-22', lowAverage: 85500.1, highAverage: 86099.248, volume: 17_000_000_000 },
]

describe('SummaryStats', () => {
  it('rounds large prices for the headline tiles and keeps the exact value on hover', () => {
    render(<SummaryStats data={btc} />)

    const highest = screen.getByText('86,099.25')
    expect(highest).toHaveAttribute('title', '86,099.2480')
    expect(screen.getByText('75,675.80')).toHaveAttribute('title', '75,675.8049')
  })

  it('keeps 4 dp for ordinary prices without a redundant title', () => {
    render(
      <SummaryStats
        data={[{ day: '2026-10-09', lowAverage: 383.0768, highAverage: 384.9723, volume: 1 }]}
      />,
    )

    expect(screen.getByText('384.9723')).not.toHaveAttribute('title')
  })

  it('summarises the period and volume', () => {
    render(<SummaryStats data={btc} />)

    expect(screen.getByText('2')).toBeInTheDocument()
    expect(screen.getByText('Sep 16 – Sep 22')).toBeInTheDocument()
    expect(screen.getByText('17.5B')).toBeInTheDocument()
  })
})

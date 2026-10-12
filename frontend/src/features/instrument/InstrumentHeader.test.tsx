import { render, screen } from '@testing-library/react'
import { InstrumentHeader } from './InstrumentHeader'
import type { Instrument } from './schema'

const tesla: Instrument = {
  symbol: 'TSLA',
  longName: 'Tesla, Inc.',
  currency: 'USD',
  exchangeName: 'NasdaqGS',
  instrumentType: 'EQUITY',
}

describe('InstrumentHeader', () => {
  it('uses the name as the heading with ticker, exchange and type above it', () => {
    render(<InstrumentHeader symbol="TSLA" instrument={tesla} loading={false} />)

    expect(screen.getByRole('heading', { level: 2 })).toHaveTextContent('Tesla, Inc.')
    expect(screen.getByText('TSLA · NasdaqGS · Stock')).toBeInTheDocument()
  })

  it('falls back to the ticker while details are unavailable', () => {
    render(<InstrumentHeader symbol="TSLA" instrument={undefined} loading={false} />)

    expect(screen.getByRole('heading', { level: 2 })).toHaveTextContent('TSLA')
  })

  it('omits the exchange code for indices', () => {
    const index = {
      ...tesla,
      symbol: '^GSPC',
      longName: 'S&P 500',
      exchangeName: 'SNP',
      instrumentType: 'INDEX',
    }
    render(<InstrumentHeader symbol="^GSPC" instrument={index} loading={false} />)

    expect(screen.getByText('^GSPC · Index')).toBeInTheDocument()
    expect(screen.queryByText(/SNP/)).not.toBeInTheDocument()
  })

  it('shows known facts under the ticker when there is no name', () => {
    const nameless = { ...tesla, longName: null }
    render(<InstrumentHeader symbol="TSLA" instrument={nameless} loading={false} />)

    expect(screen.getByRole('heading', { level: 2 })).toHaveTextContent('TSLA')
    expect(screen.getByText('NasdaqGS · Stock')).toBeInTheDocument()
  })

  it('announces loading', () => {
    render(<InstrumentHeader symbol="AAPL" instrument={undefined} loading />)

    expect(screen.getByRole('heading', { level: 2 })).toHaveTextContent('Loading AAPL…')
  })
})

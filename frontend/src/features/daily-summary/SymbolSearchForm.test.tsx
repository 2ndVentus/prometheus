import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { SymbolSearchForm } from './SymbolSearchForm'

function setup(props: Partial<Parameters<typeof SymbolSearchForm>[0]> = {}) {
  const onSearch = vi.fn<(symbol: string) => void>()
  render(<SymbolSearchForm initialSymbol="" isLoading={false} onSearch={onSearch} {...props} />)
  return { onSearch, user: userEvent.setup(), input: screen.getByLabelText('Ticker symbol') }
}

describe('SymbolSearchForm', () => {
  it('normalises the symbol and searches on Enter', async () => {
    const { onSearch, user, input } = setup()

    await user.type(input, '  tsla {Enter}')

    expect(onSearch).toHaveBeenCalledWith('TSLA')
    expect(input).toHaveValue('TSLA')
  })

  it('searches via the button', async () => {
    const { onSearch, user, input } = setup()

    await user.type(input, 'brk-b')
    await user.click(screen.getByRole('button', { name: 'Show' }))

    expect(onSearch).toHaveBeenCalledWith('BRK-B')
  })

  it('shows an inline error and does not search for an invalid symbol', async () => {
    const { onSearch, user, input } = setup()

    await user.type(input, 'TS LA{Enter}')

    expect(onSearch).not.toHaveBeenCalled()
    expect(input).toHaveAttribute('aria-invalid', 'true')
    expect(input).toHaveAccessibleDescription(/use 1–14 letters/i)
  })

  it('asks for a symbol when empty', async () => {
    const { onSearch, user } = setup()

    await user.click(screen.getByRole('button', { name: 'Show' }))

    expect(onSearch).not.toHaveBeenCalled()
    expect(screen.getByText('Enter a ticker symbol.')).toBeInTheDocument()
  })

  it('clears the error after a valid search', async () => {
    const { user, input } = setup()

    await user.type(input, '<x>{Enter}')
    await user.clear(input)
    await user.type(input, 'AAPL{Enter}')

    expect(input).toHaveAttribute('aria-invalid', 'false')
  })

  it('searches an example symbol in one click', async () => {
    const { onSearch, user, input } = setup()

    await user.click(screen.getByRole('button', { name: '^GSPC' }))

    expect(onSearch).toHaveBeenCalledWith('^GSPC')
    expect(input).toHaveValue('^GSPC')
  })

  it('stays usable while loading, so a new search can supersede a slow one', async () => {
    const { onSearch, user } = setup({ isLoading: true })

    expect(screen.getByRole('form', { name: 'Search' })).toHaveAttribute('aria-busy', 'true')
    expect(screen.getByRole('button', { name: 'Show' })).toBeEnabled()
    await user.click(screen.getByRole('button', { name: 'AAPL' }))

    expect(onSearch).toHaveBeenCalledWith('AAPL')
  })

  it('starts with the current symbol', () => {
    const { input } = setup({ initialSymbol: 'VOD.L' })

    expect(input).toHaveValue('VOD.L')
  })
})

import { getJson } from '../../api/httpClient'
import { instrumentSchema, type Instrument } from './schema'

export function getInstrument(symbol: string, signal?: AbortSignal): Promise<Instrument> {
  return getJson(`/api/v1/stocks/${encodeURIComponent(symbol)}`, instrumentSchema, signal)
}

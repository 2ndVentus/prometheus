import { useSyncExternalStore } from 'react'

/**
 * Chart colours as concrete values: SVG presentation attributes (which Recharts writes) don't
 * reliably resolve CSS custom properties, so the chart reads the same tokens as index.css from here.
 * Validated with the dataviz palette checker in both modes (all-pairs CVD ΔE ≥ 9.2 light / 9.4 dark).
 */
const LIGHT = {
  surface: '#fcfcfb',
  grid: '#e1e0d9',
  axis: '#c3c2b7',
  ink: '#0b0b0b',
  inkSecondary: '#52514e',
  inkMuted: '#898781',
  high: '#2a78d6',
  low: '#eb6834',
  volume: '#1baf7a',
}

const DARK: typeof LIGHT = {
  surface: '#1a1a19',
  grid: '#2c2c2a',
  axis: '#383835',
  ink: '#ffffff',
  inkSecondary: '#c3c2b7',
  inkMuted: '#898781',
  high: '#3987e5',
  low: '#d95926',
  volume: '#199e70',
}

export type ChartTheme = typeof LIGHT

const QUERY = '(prefers-color-scheme: dark)'

function subscribe(onChange: () => void): () => void {
  const media = window.matchMedia?.(QUERY)
  media?.addEventListener('change', onChange)
  return () => media?.removeEventListener('change', onChange)
}

const prefersDark = (): boolean => window.matchMedia?.(QUERY).matches ?? false

export function useChartTheme(): ChartTheme {
  return useSyncExternalStore(subscribe, prefersDark, () => false) ? DARK : LIGHT
}

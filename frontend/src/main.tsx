import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { App } from './App'
import './index.css'

const queryClient = new QueryClient({
  defaultOptions: {
    // Market data refreshes every 15 minutes at most; refocusing the tab shouldn't refetch.
    queries: { refetchOnWindowFocus: false },
  },
})

const rootElement = document.getElementById('root')
if (!rootElement) throw new Error('Root element #root not found')

createRoot(rootElement).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <App />
    </QueryClientProvider>
  </StrictMode>,
)

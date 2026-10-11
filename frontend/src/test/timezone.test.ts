describe('test environment', () => {
  it('runs in a timezone west of UTC', () => {
    expect(new Date('2026-10-09T00:00:00Z').getTimezoneOffset()).toBeGreaterThan(0)
  })
})

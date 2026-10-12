using StockData.Application.Domain;

namespace StockData.Application.Instruments;

public interface IInstrumentService
{
    Task<InstrumentInfo> GetInstrumentAsync(StockSymbol symbol, CancellationToken cancellationToken);
}

using System.Text.Json;
using System.Text.Json.Serialization;

namespace StockData.Api.Contracts;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(IReadOnlyList<DailySummaryResponse>))]
[JsonSerializable(typeof(InstrumentResponse))]
internal sealed partial class ApiJsonContext : JsonSerializerContext;

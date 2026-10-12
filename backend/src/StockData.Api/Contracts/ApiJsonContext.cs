using System.Text.Json;
using System.Text.Json.Serialization;

namespace StockData.Api.Contracts;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(IReadOnlyList<DailySummaryResponse>))]
internal sealed partial class ApiJsonContext : JsonSerializerContext;

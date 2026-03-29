using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace SentryIssuesAgent
{
    public class StringOrIntConverter : JsonConverter<int>
    {
        public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                var raw = reader.GetString() ?? "0";
                // Handle humanized values like "1.2k", "10k"
                if (raw.EndsWith("k", StringComparison.OrdinalIgnoreCase) &&
                    double.TryParse(raw[..^1], out var kVal))
                    return (int)(kVal * 1000);

                return int.TryParse(raw, out var val) ? val : 0;
            }
            return reader.GetInt32();
        }

        public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
            => writer.WriteNumberValue(value);
    }
}

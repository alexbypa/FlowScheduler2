using System.Globalization;
using System.Text.Json;
namespace FlowScheduler.Core.Shared;
public static class jsonHelper {
    public static int GetInt(this JsonElement element, string path, int valueOnError) {
        try {
            return (int)GetPropertyValue(element, path);
        } catch {
            return valueOnError;
        }
    }
    public static long GetLong(this JsonElement element, string path, long valueOnError) {
        try {
            var res = GetPropertyValue(element, path);
            return long.Parse(res.ToString());
            //return (long)GetPropertyValue(element, path);
        } catch {
            return valueOnError;
        }
    }
    public static string GetString(this JsonElement element, string path, string valueOnError) {
        try {
            return (string)GetPropertyValue(element, path);
        } catch {
            return valueOnError;
        }
    }
    private static object GetPropertyValue(JsonElement element, string path) {
        string[] properties = path.Split('.');

        if (properties.Length == 1) {
            if (element.TryGetProperty(properties[0], out JsonElement property)) {
                return property.ValueKind switch {
                    JsonValueKind.String => property.GetString(),
                    JsonValueKind.Number => property.GetInt32(), // Puoi adattare il tipo numerico
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Array => property.EnumerateArray(), // Restituisce un IEnumerable
                    JsonValueKind.Object => property, // Restituisce un JsonElement
                    _ => throw new InvalidOperationException($"Tipo di dato non supportato: {property.ValueKind}")
                };
            } else {
                return null; // Proprietà non trovata
            }
        }
        if (element.TryGetProperty(properties[0], out JsonElement nextElement)) {
            return GetPropertyValue(nextElement, string.Join(".", properties.Skip(1)));
        }
        return null;
    }
    public static bool EvaluateCondition(Dictionary<string, object> row, string condition) {
        try {
            var parts = condition.Split(' ');
            if (parts.Length < 3)
                return false; // Formato non valido

            string colName = parts[0];
            string op = parts[1];
            string targetVal = parts[2]; // Nota: gestiamo stringhe semplici o numeri

            if (!row.ContainsKey(colName) || row[colName] == DBNull.Value)
                return false;

            string actualValStr = Convert.ToString(row[colName], CultureInfo.InvariantCulture);

            // Gestione Numerica
            if (double.TryParse(actualValStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double actualNum) &&
                double.TryParse(targetVal, NumberStyles.Any, CultureInfo.InvariantCulture, out double targetNum)) {
                return op switch {
                    "==" => actualNum == targetNum,
                    "!=" => actualNum != targetNum,
                    ">" => actualNum > targetNum,
                    "<" => actualNum < targetNum,
                    ">=" => actualNum >= targetNum,
                    "<=" => actualNum <= targetNum,
                    _ => false
                };
            }

            // Gestione Stringa
            targetVal = targetVal.Trim('\'', '"'); // Rimuove apici se presenti es: 'Error'
            return op switch {
                "==" => actualValStr.Equals(targetVal, StringComparison.OrdinalIgnoreCase),
                "!=" => !actualValStr.Equals(targetVal, StringComparison.OrdinalIgnoreCase),
                _ => false
            };
        } catch {
            return false; // In caso di errore di parsing ignoriamo
        }
    }
}
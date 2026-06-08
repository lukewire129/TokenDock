using System.Text.Json;

namespace TokenDock.Services;

internal static class JsonElementExtensions
{
    public static JsonElement? GetPropertyOrNull(this JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) ? value : null;
    }
}

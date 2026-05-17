using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.RegularExpressions;
using TwitchTools.Web.Data;
using TwitchTools.Web.Models;

namespace TwitchTools.Web.Services;

public sealed class OverlayService(AppDbContext dbContext) : IOverlayService
{
    private static readonly Regex FieldTokenRegex = new(@"\{([A-Za-z0-9_]+)\}", RegexOptions.Compiled);

    public async Task<OverlayWidgetViewModel?> GetFollowerByTokenAsync(string overlayToken, CancellationToken cancellationToken)
    {
        var result = await dbContext.Streamers
            .Where(x => x.FollowerOverlayToken == overlayToken)
            .Select(x => new
            {
                x.OverlaySnapshot!.LastFollowerName,
                x.OverlaySnapshot.LastFollowerUtc
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (result is null)
        {
            return null;
        }

        return new OverlayWidgetViewModel
        {
            Title = "Last Follower",
            EmptyMessage = "No recent follower",
            DisplayValue = result.LastFollowerName,
            EventUtc = result.LastFollowerUtc
        };
    }

    public async Task<OverlayWidgetViewModel?> GetSubscriberByTokenAsync(string overlayToken, CancellationToken cancellationToken)
    {
        var result = await dbContext.Streamers
            .Where(x => x.SubscriberOverlayToken == overlayToken)
            .Select(x => new
            {
                x.OverlaySnapshot!.LastSubscriberName,
                x.OverlaySnapshot.LastSubscriberUtc
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (result is null)
        {
            return null;
        }

        return new OverlayWidgetViewModel
        {
            Title = "Last Subscriber",
            EmptyMessage = "No recent subscriber",
            DisplayValue = result.LastSubscriberName,
            EventUtc = result.LastSubscriberUtc
        };
    }

    public async Task<CustomOverlayWidgetRuntimeViewModel?> GetCustomWidgetByTokenAsync(string overlayToken, CancellationToken cancellationToken)
    {
        var result = await dbContext.Streamers
            .Where(x => x.CustomOverlayToken == overlayToken)
            .Select(x => new
            {
                x.CustomOverlayName,
                x.CustomOverlayHtml,
                x.CustomOverlayCss,
                x.CustomOverlayJs,
                x.CustomOverlayFieldsJson,
                x.CustomOverlayDataJson,
                x.TwitchUserId
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (result is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(result.CustomOverlayHtml)
            || string.IsNullOrWhiteSpace(result.CustomOverlayCss)
            || string.IsNullOrWhiteSpace(result.CustomOverlayJs)
            || string.IsNullOrWhiteSpace(result.CustomOverlayFieldsJson))
        {
            return null;
        }

        var fieldData = BuildFieldData(result.CustomOverlayFieldsJson, result.CustomOverlayDataJson);

        return new CustomOverlayWidgetRuntimeViewModel
        {
            OverlayToken = overlayToken,
            WidgetName = string.IsNullOrWhiteSpace(result.CustomOverlayName)
                ? "Imported StreamElements Widget"
                : result.CustomOverlayName,
            Html = ReplaceFieldTokens(result.CustomOverlayHtml, fieldData),
            Css = ReplaceFieldTokens(result.CustomOverlayCss, fieldData),
            Js = ReplaceFieldTokens(result.CustomOverlayJs, fieldData),
            FieldDataJson = JsonSerializer.Serialize(fieldData),
            ChannelProviderId = string.IsNullOrWhiteSpace(result.TwitchUserId) ? "0" : result.TwitchUserId
        };
    }

    public async Task SaveCustomWidgetAsync(string ownerSubject, CustomOverlayWidgetInput input, CancellationToken cancellationToken)
    {
        var streamer = await dbContext.Streamers.FirstOrDefaultAsync(x => x.OwnerSubject == ownerSubject, cancellationToken);
        if (streamer is null)
        {
            throw new InvalidOperationException("Save Twitch profile settings first before creating custom overlays.");
        }

        if (string.IsNullOrWhiteSpace(input.Html)
            || string.IsNullOrWhiteSpace(input.Css)
            || string.IsNullOrWhiteSpace(input.Js)
            || string.IsNullOrWhiteSpace(input.FieldsJson))
        {
            throw new InvalidOperationException("HTML, CSS, JS and FIELDS are required.");
        }

        EnsureJsonObject(input.FieldsJson, "FIELDS");
        EnsureJsonObject(string.IsNullOrWhiteSpace(input.DataJson) ? "{}" : input.DataJson, "DATA");

        streamer.CustomOverlayToken = EnsureToken(streamer.CustomOverlayToken);
        streamer.CustomOverlayName = string.IsNullOrWhiteSpace(input.Name) ? "Imported StreamElements Widget" : input.Name.Trim();
        streamer.CustomOverlayHtml = input.Html.Trim();
        streamer.CustomOverlayCss = input.Css.Trim();
        streamer.CustomOverlayJs = input.Js.Trim();
        streamer.CustomOverlayFieldsJson = input.FieldsJson.Trim();
        streamer.CustomOverlayDataJson = string.IsNullOrWhiteSpace(input.DataJson) ? "{}" : input.DataJson.Trim();
        streamer.CustomOverlayUpdatedUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static Dictionary<string, object?> BuildFieldData(string fieldsJson, string? dataJson)
    {
        var output = new Dictionary<string, object?>(StringComparer.Ordinal);

        using (var fieldsDoc = JsonDocument.Parse(fieldsJson))
        {
            foreach (var property in fieldsDoc.RootElement.EnumerateObject())
            {
                var value = property.Value;
                if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("value", out var nestedValue))
                {
                    output[property.Name] = ToObject(nestedValue);
                    continue;
                }

                output[property.Name] = ToObject(value);
            }
        }

        if (!string.IsNullOrWhiteSpace(dataJson))
        {
            using var dataDoc = JsonDocument.Parse(dataJson);
            if (dataDoc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in dataDoc.RootElement.EnumerateObject())
                {
                    output[property.Name] = ToObject(property.Value);
                }
            }
        }

        return output;
    }

    private static string ReplaceFieldTokens(string input, IReadOnlyDictionary<string, object?> fieldData)
    {
        return FieldTokenRegex.Replace(input, match =>
        {
            var key = match.Groups[1].Value;
            if (!fieldData.TryGetValue(key, out var value))
            {
                return match.Value;
            }

            if (value is null)
            {
                return string.Empty;
            }

            return value switch
            {
                bool booleanValue => booleanValue ? "true" : "false",
                _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty
            };
        });
    }

    private static object? ToObject(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.TryGetInt64(out var longValue)
                ? longValue
                : value.TryGetDecimal(out var decimalValue)
                    ? decimalValue
                    : value.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => value.GetRawText()
        };
    }

    private static void EnsureJsonObject(string rawJson, string sectionName)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException($"{sectionName} must be a JSON object.");
            }
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"{sectionName} is not valid JSON: {ex.Message}");
        }
    }

    private static string EnsureToken(string? token)
    {
        if (!string.IsNullOrWhiteSpace(token))
        {
            return token;
        }

        return Guid.NewGuid().ToString("N");
    }
}